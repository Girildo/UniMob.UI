using System;
using System.Collections.Generic;
using UniMob.UI.Diagnostics;
using UniMob.UI.Internal.Views;
using UnityEngine;

namespace UniMob.UI.Rendering
{
    // ================================================================================================
    //  Virtualized 2D grid -- the row-granular sibling of RenderSliverList.
    // ================================================================================================
    //
    //  Read the architecture notes at the top of RenderSliverList.cs first: this render object reuses the
    //  same eager/lazy split, the same measured-extent + EMA estimation, the same "one function is the
    //  single source of truth for where item N is" discipline, and the same window selection through
    //  that model. The one difference: the UNIT OF VIRTUALIZATION IS THE ROW, not the item.
    //
    //  A SliverGridDelegate turns the viewport's available cross-axis extent into a regular tile layout
    //  (SliverGridLayout): a fixed column count N and a cell cross-extent, so item i is deterministically
    //  at (row i/N, column i%N). That determinism is what lets the grid lazily build/measure only the rows
    //  near the viewport, exactly as the list builds only the items near it.
    //
    //  Two main-axis modes (chosen by the delegate):
    //    * Fixed (SliverGridLayout.IsFixedMainAxis): every cell has the same known main-axis extent, so a
    //      row's leading edge is an exact arithmetic function of its index -- no measuring, no estimation,
    //      and the drift pitfall from the list simply cannot occur.
    //    * Measured rows: each row is as tall as its tallest built child. Exact for rows ever measured
    //      (kept in _measuredRowExtents even after they leave the window) and EMA-estimated
    //      (_averageRowExtent) for the rest -- the row-level analog of the list's per-item model.
    //
    //  EstimateRowLeadingEdgeOffset(row) is the single source of truth both modes route through, so
    //  positioning, TotalContentSize(), and scroll-to always agree, and EstimateRowAt(offset) is its
    //  inverse, which window selection routes through.
    // ================================================================================================

    public class RenderSliverGrid : MultiChildRenderObject, IScrollableRenderObject
    {
        private readonly ISliverGridState _state;
        private readonly List<IndexedLayoutData> _visibleChildrenIndexed = new();

        private Vector2 _viewportSize;
        private float _virtualizationCacheExtent;

        // The regular-tile geometry resolved from the delegate this pass (column count, cell cross-extent,
        // optional fixed main extent + spacing). CrossAxisCount == 0 means "no pass has run yet".
        private SliverGridLayout _layout;

        // --- Measured-rows mode (delegate returned no fixed main extent) ---
        // Exact main-axis extent of every ROW ever measured, kept even after it leaves the build window --
        // the row-level analog of RenderSliverList._measuredExtents.
        private readonly MeasuredExtents _measuredRowExtents = new();
        private readonly Dictionary<int, float> _rowHeightsThisPass = new();
        private const float DefaultEstimatedExtent = 100f;
        private const float AverageExtentSmoothing = 0.25f;
        private float _averageRowExtent = DefaultEstimatedExtent;

        // The window built+measured by the last PerformSizing() pass, reused by PerformPositioning() so it
        // never re-runs the ItemBuilder. Each cell keeps its logical index and measured main extent; its
        // cross-extent is uniform (_layout.CellCrossAxisExtent). Culled against the offset it was selected
        // for, not the live one -- see RenderSliverList's window notes.
        private readonly List<BuiltCell> _windowCells = new();
        private float _windowScrollOffset;

        private readonly struct BuiltCell
        {
            public BuiltCell(int index, float mainExtent)
            {
                Index = index;
                MainExtent = mainExtent;
            }

            public int Index { get; }
            public float MainExtent { get; }
        }

        /// <summary>A scrollable positions its children beyond the viewport; that is what scrolling is.</summary>
        protected override bool ChildrenMayOverhang => true;

        public RenderSliverGrid(ISliverGridState state)
            : base(state)
        {
            _state = state;
        }

        private float ComputeVirtualizationCacheExtent()
        {
            var isHorizontal = _state.Axis == Axis.Horizontal;
            return isHorizontal ? _viewportSize.x : _viewportSize.y;
        }

        // --- Axis-aware padding: the grid works in (main, cross) space; map to the widget's x/y padding. ---
        private float MainStartPadding(bool isHorizontal) =>
            isHorizontal ? _state.Padding.Left : _state.Padding.Top;

        private float MainEndPadding(bool isHorizontal) =>
            isHorizontal ? _state.Padding.Right : _state.Padding.Bottom;

        private float CrossStartPadding(bool isHorizontal) =>
            isHorizontal ? _state.Padding.Top : _state.Padding.Left;

        private float CrossEndPadding(bool isHorizontal) =>
            isHorizontal ? _state.Padding.Bottom : _state.Padding.Right;

        /// <summary>
        ///     SIZING PASS: resolves the cross-axis geometry from the delegate, then (eager) measures every
        ///     child or (lazy) builds and measures just the rows covering [viewport +/- cache].
        /// </summary>
        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            var axis = _state.Axis;
            var isHorizontal = axis == Axis.Horizontal;
            var isVertical = !isHorizontal;

            // A grid needs both axes bounded, and for different reasons: the scroll axis to have a
            // viewport to scroll through, the cross axis to divide into columns at all. Reported and
            // materialised to zero rather than thrown -- a throw from inside a sizing pass reaches the
            // developer as a LogException with no widget path, no context object and no remedy.
            var unbounded = LayoutAxes.None;

            if (isHorizontal && !constraints.HasBoundedWidth)
            {
                unbounded |= LayoutAxes.Horizontal;
            }

            if (isVertical && !constraints.HasBoundedHeight)
            {
                unbounded |= LayoutAxes.Vertical;
            }

            var crossAxis = isHorizontal ? Axis.Vertical : Axis.Horizontal;
            if (!float.IsFinite(constraints.MaxAlongAxis(crossAxis)))
            {
                unbounded |=
                    crossAxis == Axis.Horizontal ? LayoutAxes.Horizontal : LayoutAxes.Vertical;
            }

            if (unbounded != LayoutAxes.None)
            {
                ReportUnboundedConstraint(unbounded, constraints, BoundBothAxes);
            }

            _viewportSize = constraints.Largest.ZeroOn(unbounded);

            if (
                _state.VirtualizationCacheExtent.HasValue
                && _state.VirtualizationCacheExtent.Value < 0
            )
                throw new InvalidOperationException(
                    "VirtualizationCacheExtent cannot be negative."
                );
            _virtualizationCacheExtent =
                _state.VirtualizationCacheExtent ?? ComputeVirtualizationCacheExtent();

            var viewportCross = isHorizontal ? _viewportSize.y : _viewportSize.x;
            var availableCross = Mathf.Max(
                0f,
                viewportCross - CrossStartPadding(isHorizontal) - CrossEndPadding(isHorizontal)
            );

            _layout = _state.GridDelegate.GetLayout(availableCross);

            if (_state.ItemCount.HasValue)
                PerformLazySizing(isHorizontal, isVertical);
            else
                PerformEagerSizing(isHorizontal, isVertical);

            return _viewportSize;
        }

        private void PerformEagerSizing(bool isHorizontal, bool isVertical)
        {
            _windowCells.Clear();
            // Eager re-measures the whole grid each pass, so the row model is rebuilt from scratch -- unlike
            // lazy mode, which must retain rows across passes (that's the point of _measuredRowExtents).
            _measuredRowExtents.Clear();

            var rowAverage = MeasureWindow(
                _state.AllChildren,
                startIndex: 0,
                isHorizontal,
                isVertical
            );
            UpdateAverageRowExtent(rowAverage, hadPriorMeasurement: false);

            _windowScrollOffset = SliverLayoutMath.OffsetWithinContent(
                _state.ScrollPixelOffset,
                TotalContentSize(),
                isHorizontal ? _viewportSize.x : _viewportSize.y
            );
        }

        /// <summary>
        ///     Builds and measures the rows covering [scrollOffset - cache, scrollOffset + viewport + cache].
        ///     The rows are selected through <see cref="EstimateRowLeadingEdgeOffset" />, the model
        ///     positioning places them with, and corrected until the measured rows cover the viewport.
        /// </summary>
        private void PerformLazySizing(bool isHorizontal, bool isVertical)
        {
            var itemCount = _state.ItemCount!.Value;
            _windowCells.Clear();

            if (itemCount <= 0)
            {
                _state.RequestBuildWindow(0, 0);
                _windowScrollOffset = _state.ScrollPixelOffset;
                return;
            }

            var n = _layout.CrossAxisCount;
            var rowCount = _layout.RowCount(itemCount);
            var mainSpacing = _layout.MainAxisSpacing;
            var cacheExtent = _virtualizationCacheExtent;
            var viewportMain = isHorizontal ? _viewportSize.x : _viewportSize.y;

            var scrollOffset = SliverLayoutMath.OffsetWithinContent(
                _state.ScrollPixelOffset,
                TotalContentSize(),
                viewportMain
            );
            var startRow = EstimateRowAt(scrollOffset - cacheExtent, rowCount);
            var endRow = EstimateRowAt(scrollOffset + viewportMain + cacheExtent, rowCount) + 1;

            var hadPriorMeasurement = _measuredRowExtents.Count > 0;
            var rowAverage = MeasureRows(startRow, endRow, n, itemCount, isHorizontal, isVertical);
            UpdateAverageRowExtent(rowAverage, hadPriorMeasurement);

            // Measuring moves the model the rows were selected through: the rows it holds are now
            // exact, and the new average shifts every unmeasured row below them.
            for (var round = 0; round < SliverLayoutMath.MaxWindowCorrections; round++)
            {
                scrollOffset = SliverLayoutMath.OffsetWithinContent(
                    _state.ScrollPixelOffset,
                    TotalContentSize(),
                    viewportMain
                );
                var coverStart = scrollOffset - cacheExtent;
                var coverEnd = scrollOffset + viewportMain + cacheExtent;

                var corrected = SliverLayoutMath.CorrectWindow(
                    (startRow, endRow),
                    rowCount,
                    windowStartEdge: EstimateRowLeadingEdgeOffset(startRow),
                    windowEndEdge: EstimateRowLeadingEdgeOffset(endRow) - mainSpacing,
                    viewportStart: scrollOffset,
                    viewportEnd: scrollOffset + viewportMain,
                    wanted: (
                        EstimateRowAt(coverStart, rowCount),
                        EstimateRowAt(coverEnd, rowCount) + 1
                    ),
                    coverEnd,
                    localStride: (_layout.CellMainAxisExtent ?? rowAverage) + mainSpacing
                );

                if (corrected == (startRow, endRow))
                    break;

                (startRow, endRow) = corrected;
                rowAverage = MeasureRows(startRow, endRow, n, itemCount, isHorizontal, isVertical);
            }

            _windowScrollOffset = scrollOffset;
        }

        // Builds and measures the cells of rows [startRow, endRow); answers their average row extent.
        private float MeasureRows(
            int startRow,
            int endRow,
            int crossAxisCount,
            int itemCount,
            bool isHorizontal,
            bool isVertical
        )
        {
            var startIndex = startRow * crossAxisCount;
            var endIndex = Mathf.Min(endRow * crossAxisCount, itemCount);

            var builtStates = _state.RequestBuildWindow(startIndex, endIndex);
            return MeasureWindow(builtStates, startIndex, isHorizontal, isVertical);
        }

        // Measures a contiguous run of built children (all children in eager mode; the window in lazy mode)
        // into _windowCells, recording each cell's main extent and -- in measured-rows mode -- each row's
        // height. Answers the average extent of the rows it measured, or 0 when it measured none.
        private float MeasureWindow(
            IState[] builtStates,
            int startIndex,
            bool isHorizontal,
            bool isVertical
        )
        {
            var cellCross = _layout.CellCrossAxisExtent;
            var cellMain = _layout.CellMainAxisExtent;
            var childConstraints = MakeCellConstraints(isHorizontal, cellCross, cellMain);
            var measuring = !_layout.IsFixedMainAxis;

            _windowCells.Clear();
            _rowHeightsThisPass.Clear();

            for (var i = 0; i < builtStates.Length; i++)
            {
                var index = startIndex + i;
                var childSize = LayoutChild(builtStates[i], childConstraints);
                SliverLayoutMath.ThrowIfUnconstrainedMainAxis(childSize, isHorizontal, isVertical);

                // Fixed mode forces the cell size via tight constraints; measured mode reads the child's
                // natural main-axis size (its cross-axis size is always tight to the cell).
                var mainExtent = cellMain ?? (isHorizontal ? childSize.x : childSize.y);
                _windowCells.Add(new BuiltCell(index, mainExtent));

                if (measuring)
                {
                    var row = _layout.RowOf(index);
                    _rowHeightsThisPass.TryGetValue(row, out var current);
                    _rowHeightsThisPass[row] = Mathf.Max(current, mainExtent);
                }
            }

            if (!measuring || _rowHeightsThisPass.Count == 0)
                return 0f;

            var sum = 0f;
            foreach (var pair in _rowHeightsThisPass)
            {
                _measuredRowExtents.Set(pair.Key, pair.Value);
                sum += pair.Value;
            }

            return sum / _rowHeightsThisPass.Count;
        }

        // Nudges the estimate toward a pass's row average (EMA) -- the same damping RenderSliverList uses
        // so a small window catching/missing a tall row between passes doesn't make offsets jump. Skips
        // the damping on the very first real measurement.
        private void UpdateAverageRowExtent(float passAverage, bool hadPriorMeasurement)
        {
            if (_layout.IsFixedMainAxis || _windowCells.Count == 0)
                return;

            _averageRowExtent = hadPriorMeasurement
                ? Mathf.Lerp(_averageRowExtent, passAverage, AverageExtentSmoothing)
                : passAverage;
        }

        // Cell constraints: tight on the cross axis (cells fill their column exactly) and, on the main axis,
        // tight to the fixed extent or loose (measured). Distinct from SliverLayoutMath.MakeChildConstraints,
        // whose cross axis stretches to the whole viewport (a list has one column, a grid has N).
        private static LayoutConstraints MakeCellConstraints(
            bool isHorizontal,
            float cellCross,
            float? cellMain
        )
        {
            var mainMax = cellMain ?? float.PositiveInfinity;
            var mainMin = cellMain ?? 0f;

            return isHorizontal
                // Horizontal grid: main = width (loose/tight), cross = height (tight to the cell).
                ? new LayoutConstraints(mainMin, cellCross, mainMax, cellCross)
                // Vertical grid: main = height (loose/tight), cross = width (tight to the cell).
                : new LayoutConstraints(cellCross, mainMin, cellCross, mainMax);
        }

        /// <summary>
        ///     THE single source of truth for main-axis geometry: the pixel offset of <paramref name="row" />'s
        ///     leading edge. Exact arithmetic in fixed mode; measured-where-known + <see cref="_averageRowExtent" />
        ///     otherwise. Positioning, <see cref="TotalContentSize" />, and scroll-to all route through it.
        /// </summary>
        private float EstimateRowLeadingEdgeOffset(int row)
        {
            var mainSpacing = _layout.MainAxisSpacing;
            var mainStartPad = MainStartPadding(_state.Axis == Axis.Horizontal);

            if (_layout.IsFixedMainAxis)
                return mainStartPad + row * (_layout.CellMainAxisExtent!.Value + mainSpacing);

            return mainStartPad
                + _measuredRowExtents.LeadingEdge(row, _averageRowExtent, mainSpacing);
        }

        /// <summary>
        ///     The inverse of <see cref="EstimateRowLeadingEdgeOffset" />: the last row that starts at or
        ///     before <paramref name="offset" />. Window selection goes through it, so the rows it picks
        ///     are the rows positioning places at that offset.
        /// </summary>
        private int EstimateRowAt(float offset, int rowCount)
        {
            var mainSpacing = _layout.MainAxisSpacing;
            var contentOffset = offset - MainStartPadding(_state.Axis == Axis.Horizontal);

            if (!_layout.IsFixedMainAxis)
                return _measuredRowExtents.IndexAt(
                    contentOffset,
                    rowCount,
                    _averageRowExtent,
                    mainSpacing
                );

            return SliverLayoutMath.SlotAtUniformStride(
                contentOffset,
                _layout.CellMainAxisExtent!.Value + mainSpacing,
                rowCount
            );
        }

        private float RowMainExtent(int row)
        {
            if (_layout.IsFixedMainAxis)
                return _layout.CellMainAxisExtent!.Value;
            return _measuredRowExtents.TryGet(row, out var height) ? height : _averageRowExtent;
        }

        /// <summary>
        ///     POSITIONING PASS: places each built cell at its row's leading edge (from
        ///     <see cref="EstimateRowLeadingEdgeOffset" />) and column's cross offset, then culls to the rows
        ///     intersecting the viewport plus cache along the scrolling axis (the cross axis always fits).
        /// </summary>
        protected override void PerformPositioning(Vector2 size)
        {
            var visible = _visibleChildrenIndexed;
            visible.Clear();

            var isHorizontal = _state.Axis == Axis.Horizontal;
            var cellCross = _layout.CellCrossAxisExtent;
            var crossStartPad = CrossStartPadding(isHorizontal);
            var viewportMainAxisSize = isHorizontal ? _viewportSize.x : _viewportSize.y;

            var viewportStart = _windowScrollOffset - _virtualizationCacheExtent;
            var viewportEnd =
                _windowScrollOffset + viewportMainAxisSize + _virtualizationCacheExtent;

            for (var w = 0; w < _windowCells.Count; w++)
            {
                var cell = _windowCells[w];
                var row = _layout.RowOf(cell.Index);
                var column = _layout.ColumnOf(cell.Index);

                var mainPos = EstimateRowLeadingEdgeOffset(row);
                var mainSize = cell.MainExtent;

                // Visible if the cell intersects the viewport (plus buffer) along the scrolling axis.
                if (mainPos + mainSize <= viewportStart || mainPos >= viewportEnd)
                    continue;

                var crossPos = crossStartPad + _layout.CrossAxisOffsetForColumn(column);

                visible.Add(
                    new IndexedLayoutData
                    {
                        ChildIndex = cell.Index,
                        Layout = new LayoutInfo
                        {
                            Size = isHorizontal
                                ? new Vector2(mainSize, cellCross)
                                : new Vector2(cellCross, mainSize),
                            Position = isHorizontal
                                ? new Vector2(mainPos, crossPos)
                                : new Vector2(crossPos, mainPos),
                        },
                    }
                );
            }

            ChildrenLayoutBuffer.Clear();
            foreach (var child in visible)
                ChildrenLayoutBuffer.Add(child.Layout);

            _state.SetVisibleChildren(visible);
        }

        // The scrollable content size the View sizes its content rect to -- content only, cache extent
        // excluded (see the same note on RenderSliverList.TotalContentSize). The content ends at the leading
        // edge of the row one past the last, minus the trailing gap that edge adds, plus the end padding.
        public float TotalContentSize()
        {
            if (_layout.CrossAxisCount <= 0)
                return 0;

            var itemCount = _state.ItemCount ?? _state.AllChildren.Length;
            var rowCount = _layout.RowCount(itemCount);
            if (rowCount <= 0)
                return 0;

            var isHorizontal = _state.Axis == Axis.Horizontal;
            return EstimateRowLeadingEdgeOffset(rowCount)
                - _layout.MainAxisSpacing
                + MainEndPadding(isHorizontal);
        }

        /// <summary>
        ///     Target scroll offset in pixels to bring <paramref name="index" />'s row into view at
        ///     <paramref name="position" />. Routes through the same row model as positioning, so a scroll-to
        ///     lands where the row is actually placed.
        /// </summary>
        public float CalculateScrollPixelOffset(int index, ScrollToPosition position)
        {
            if (_layout.CrossAxisCount <= 0)
                return 0;

            var isHorizontal = _state.Axis == Axis.Horizontal;
            var viewportSize = isHorizontal ? _viewportSize.x : _viewportSize.y;
            if (index < 0)
                return 0;

            var row = _layout.RowOf(index);
            var leadingEdge = EstimateRowLeadingEdgeOffset(row);
            var rowExtent = RowMainExtent(row);

            // An index at or past the laid-out count is one the source already holds and the grid has not
            // been rebuilt for yet. The content it is about to have reaches at least that row's trailing
            // edge, so the range must not stop short of it.
            var contentSize = Mathf.Max(TotalContentSize(), leadingEdge + rowExtent);
            var totalScrollableDist = contentSize - viewportSize;
            if (totalScrollableDist <= 0)
                return 0;

            var offset = SliverLayoutMath.AlignToScrollPosition(
                leadingEdge,
                rowExtent,
                viewportSize,
                _state.ScrollPixelOffset,
                position
            );
            return Mathf.Clamp(offset, 0, totalScrollableDist);
        }

        // Intrinsic sizing reports the total main-axis size; the cross axis stretches to its constraint, so
        // that axis returns 0. Uses the current estimate rather than forcing a full measurement (which would
        // defeat laziness). Returns 0 before the first layout pass has resolved a grid geometry.
        protected override float ComputeIntrinsicHeight(float width) =>
            _state.Axis == Axis.Horizontal ? 0 : TotalContentSize();

        protected override float ComputeIntrinsicWidth(float height) =>
            _state.Axis == Axis.Vertical ? 0 : TotalContentSize();

        private const string BoundBothAxes =
            "A ScrollGrid needs a bounded viewport on its scroll axis and a bounded cross axis to "
            + "divide into columns. Wrap it in Expanded or give an ancestor a fixed size on that axis.";
    }
}
