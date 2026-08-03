using System;
using System.Collections.Generic;
using UniMob.UI.Layout.Internal.Views;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    // ================================================================================================
    //  Virtualized 2D grid -- the row-granular sibling of RenderSliverList.
    // ================================================================================================
    //
    //  Read the architecture notes at the top of RenderSliverList.cs first: this render object reuses the
    //  same eager/lazy split, the same measured-extent + EMA estimation, the same "one function is the
    //  single source of truth for where item N is" discipline, and the same one-settle-frame window
    //  approximation. The one difference: the UNIT OF VIRTUALIZATION IS THE ROW, not the item.
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
    //  positioning, TotalContentSize(), and scroll-to always agree (the list's pitfall (1), avoided here).
    // ================================================================================================

    public interface ISliverGridState : IMultiChildLayoutState
    {
        /// <summary>Eager mode only (<see cref="ItemCount" /> is null); empty otherwise.</summary>
        IState[] AllChildren { get; }

        /// <summary>Lazy mode only; the full logical item count. Null in eager mode.</summary>
        int? ItemCount { get; }

        Axis Axis { get; }

        /// <summary>Current absolute scroll offset in pixels along the scrolling axis.</summary>
        float ScrollPixelOffset { get; }

        float? VirtualizationCacheExtent { get; }

        /// <summary>Decides the cross-axis geometry (column count, cell size) each layout pass.</summary>
        SliverGridDelegate GridDelegate { get; }

        /// <summary>Padding around the grid content, in the widget's own x/y space.</summary>
        RectPadding Padding { get; }

        void SetVisibleChildren(List<IndexedLayoutData> visibleChildren);

        /// <summary>
        ///     Lazy mode only. Ensures indices in [startIndexInclusive, endIndexExclusive) are built and
        ///     returns their states in index order. Same contract as <see cref="ISliverState.RequestBuildWindow" />.
        /// </summary>
        IState[] RequestBuildWindow(int startIndexInclusive, int endIndexExclusive);
    }

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
        private readonly Dictionary<int, float> _measuredRowExtents = new();
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

        public RenderSliverGrid(ISliverGridState state) : base(state.StateLifetime)
        {
            _state = state;
        }


        private float ComputeVirtualizationCacheExtent()
        {
            var isHorizontal = _state.Axis == Axis.Horizontal;
            return isHorizontal ? _viewportSize.x : _viewportSize.y;
        }

        // --- Axis-aware padding: the grid works in (main, cross) space; map to the widget's x/y padding. ---
        private float MainStartPadding(bool isHorizontal) => isHorizontal ? _state.Padding.Left : _state.Padding.Top;
        private float MainEndPadding(bool isHorizontal) => isHorizontal ? _state.Padding.Right : _state.Padding.Bottom;
        private float CrossStartPadding(bool isHorizontal) => isHorizontal ? _state.Padding.Top : _state.Padding.Left;
        private float CrossEndPadding(bool isHorizontal) => isHorizontal ? _state.Padding.Bottom : _state.Padding.Right;

        /// <summary>
        ///     SIZING PASS: resolves the cross-axis geometry from the delegate, then (eager) measures every
        ///     child or (lazy) builds and measures just the rows covering [viewport +/- cache].
        /// </summary>
        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            var axis = _state.Axis;
            var isHorizontal = axis == Axis.Horizontal;
            var isVertical = !isHorizontal;

            if (isHorizontal && !constraints.HasBoundedWidth)
                throw new InvalidOperationException(
                    "A horizontal ScrollGrid must have a bounded width. " +
                    "This usually means it is being placed inside another horizontal scrollable or a Row without an Expanded widget."
                );

            if (isVertical && !constraints.HasBoundedHeight)
                throw new InvalidOperationException(
                    "A vertical ScrollGrid must have a bounded height. " +
                    "This usually means it is being placed inside another vertical scrollable or a Column without an Expanded widget."
                );

            var crossAxis = isHorizontal ? Axis.Vertical : Axis.Horizontal;
            if (!float.IsFinite(constraints.MaxAlongAxis(crossAxis)))
                throw new InvalidOperationException(
                    "A ScrollGrid must have a bounded cross axis to compute its column count. " +
                    "Give it a finite width (vertical grid) or height (horizontal grid)."
                );

            _viewportSize = constraints.Largest;

            if (_state.VirtualizationCacheExtent.HasValue && _state.VirtualizationCacheExtent.Value < 0)
                throw new InvalidOperationException("VirtualizationCacheExtent cannot be negative.");
            _virtualizationCacheExtent = _state.VirtualizationCacheExtent ?? ComputeVirtualizationCacheExtent();

            var viewportCross = isHorizontal ? _viewportSize.y : _viewportSize.x;
            var availableCross = Mathf.Max(0f,
                viewportCross - CrossStartPadding(isHorizontal) - CrossEndPadding(isHorizontal));

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

            MeasureWindow(_state.AllChildren, startIndex: 0, isHorizontal, isVertical);
            _windowScrollOffset = _state.ScrollPixelOffset;
        }

        /// <summary>
        ///     Picks the row range covering [scrollOffset - cache, scrollOffset + viewport + cache] via a
        ///     uniform row-stride guess (fixed cell extent if known, else <see cref="_averageRowExtent" />),
        ///     converts it to an item index range, then builds and measures exactly that window. Same
        ///     one-settle-frame approximation as the list.
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
            var mainStartPad = MainStartPadding(isHorizontal);
            var cacheExtent = _virtualizationCacheExtent;
            var viewportMain = isHorizontal ? _viewportSize.x : _viewportSize.y;
            var scrollOffset = _state.ScrollPixelOffset;

            var rowStride = (_layout.CellMainAxisExtent ?? _averageRowExtent) + mainSpacing;

            // Offsets are measured from the content origin, which starts after the leading main padding.
            var startRow = Mathf.Clamp(
                Mathf.FloorToInt((scrollOffset - cacheExtent - mainStartPad) / rowStride), 0, rowCount - 1);
            var endRow = Mathf.Clamp(
                Mathf.CeilToInt((scrollOffset + viewportMain + cacheExtent - mainStartPad) / rowStride),
                startRow + 1, rowCount);

            var startIndex = startRow * n;
            var endIndex = Mathf.Min(endRow * n, itemCount);

            var builtStates = _state.RequestBuildWindow(startIndex, endIndex);
            MeasureWindow(builtStates, startIndex, isHorizontal, isVertical);

            _windowScrollOffset = scrollOffset;
        }

        // Measures a contiguous run of built children (all children in eager mode; the window in lazy mode),
        // recording each cell's main extent and -- in measured-rows mode -- each row's height and the EMA.
        private void MeasureWindow(IState[] builtStates, int startIndex, bool isHorizontal, bool isVertical)
        {
            var cellCross = _layout.CellCrossAxisExtent;
            var cellMain = _layout.CellMainAxisExtent;
            var childConstraints = MakeCellConstraints(isHorizontal, cellCross, cellMain);
            var measuring = !_layout.IsFixedMainAxis;

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
                return;

            // Commit this pass's exact row heights, then nudge the estimate toward the pass average (EMA) --
            // the same damping RenderSliverList uses so a small window catching/missing a tall row between
            // passes doesn't make offsets jump. Skip the damping on the very first real measurement.
            var hadPriorMeasurement = _measuredRowExtents.Count > 0;
            var sum = 0f;
            foreach (var pair in _rowHeightsThisPass)
            {
                _measuredRowExtents[pair.Key] = pair.Value;
                sum += pair.Value;
            }

            var passAverage = sum / _rowHeightsThisPass.Count;
            _averageRowExtent = hadPriorMeasurement
                ? Mathf.Lerp(_averageRowExtent, passAverage, AverageExtentSmoothing)
                : passAverage;
        }

        // Cell constraints: tight on the cross axis (cells fill their column exactly) and, on the main axis,
        // tight to the fixed extent or loose (measured). Distinct from SliverLayoutMath.MakeChildConstraints,
        // whose cross axis stretches to the whole viewport (a list has one column, a grid has N).
        private static LayoutConstraints MakeCellConstraints(bool isHorizontal, float cellCross, float? cellMain)
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

            var measuredSumBelow = 0f;
            var measuredCountBelow = 0;
            foreach (var pair in _measuredRowExtents)
            {
                if (pair.Key >= row) continue;
                measuredSumBelow += pair.Value;
                measuredCountBelow++;
            }

            return mainStartPad + measuredSumBelow + _averageRowExtent * (row - measuredCountBelow) +
                   mainSpacing * row;
        }

        private float RowMainExtent(int row)
        {
            if (_layout.IsFixedMainAxis)
                return _layout.CellMainAxisExtent!.Value;
            return _measuredRowExtents.TryGetValue(row, out var height) ? height : _averageRowExtent;
        }

        /// <summary>
        ///     POSITIONING PASS: places each built cell at its row's leading edge (from
        ///     <see cref="EstimateRowLeadingEdgeOffset" />) and column's cross offset, then culls to the rows
        ///     intersecting the viewport plus cache along the scrolling axis (the cross axis always fits).
        /// </summary>
        protected override void PerformPositioning()
        {
            var visible = _visibleChildrenIndexed;
            visible.Clear();

            var isHorizontal = _state.Axis == Axis.Horizontal;
            var cellCross = _layout.CellCrossAxisExtent;
            var crossStartPad = CrossStartPadding(isHorizontal);
            var viewportMainAxisSize = isHorizontal ? _viewportSize.x : _viewportSize.y;

            var viewportStart = _windowScrollOffset - _virtualizationCacheExtent;
            var viewportEnd = _windowScrollOffset + viewportMainAxisSize + _virtualizationCacheExtent;

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

                visible.Add(new IndexedLayoutData
                {
                    ChildIndex = cell.Index,
                    Layout = new LayoutInfo
                    {
                        Size = isHorizontal ? new Vector2(mainSize, cellCross) : new Vector2(cellCross, mainSize),
                        Position = isHorizontal ? new Vector2(mainPos, crossPos) : new Vector2(crossPos, mainPos),
                    },
                });
            }

            ChildrenLayoutBuffer.Clear();
            foreach (var child in visible) ChildrenLayoutBuffer.Add(child.Layout);

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
            return EstimateRowLeadingEdgeOffset(rowCount) - _layout.MainAxisSpacing + MainEndPadding(isHorizontal);
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
            var itemCount = _state.ItemCount ?? _state.AllChildren.Length;

            if (index < 0 || index >= itemCount)
                return 0;

            var totalScrollableDist = TotalContentSize() - viewportSize;
            if (totalScrollableDist <= 0)
                return 0;

            var row = _layout.RowOf(index);
            var leadingEdge = EstimateRowLeadingEdgeOffset(row);
            var offset = SliverLayoutMath.AlignToScrollPosition(leadingEdge, RowMainExtent(row), viewportSize, position);
            return Mathf.Clamp(offset, 0, totalScrollableDist);
        }

        // Intrinsic sizing reports the total main-axis size; the cross axis stretches to its constraint, so
        // that axis returns 0. Uses the current estimate rather than forcing a full measurement (which would
        // defeat laziness). Returns 0 before the first layout pass has resolved a grid geometry.
        protected override float ComputeIntrinsicHeight(float width)
            => _state.Axis == Axis.Horizontal ? 0 : TotalContentSize();

        protected override float ComputeIntrinsicWidth(float height)
            => _state.Axis == Axis.Vertical ? 0 : TotalContentSize();
    }
}
