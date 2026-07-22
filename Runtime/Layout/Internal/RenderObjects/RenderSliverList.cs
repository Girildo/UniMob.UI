using System;
using System.Collections.Generic;
using UniMob.UI.Layout.Internal.Views;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    // A simple struct to pair a child's original index with its final layout data.
    public struct IndexedLayoutData
    {
        public int ChildIndex;
        public LayoutInfo Layout;
    }

    public interface ISliverState : IMultiChildLayoutState
    {
        /// <summary>Eager mode only (<see cref="ItemCount"/> is null); empty otherwise.</summary>
        IState[] AllChildren { get; }

        /// <summary>Lazy mode only; the full logical item count. Null in eager mode.</summary>
        int? ItemCount { get; }

        /// <summary>Lazy mode only; when set, every item has exactly this extent (no measuring/estimating needed).</summary>
        float? ItemExtent { get; }

        Axis Axis { get; }
        //Vector2 ViewportSize { get; }

        /// <summary>
        ///     Current scroll offset in pixels along the scrolling axis. An absolute value, not a 0..1 ratio --
        ///     see <see cref="UniMob.UI.ScrollController.PixelOffset"/> for why this render object needs that.
        /// </summary>
        float ScrollPixelOffset { get; }
        float? VirtualizationCacheExtent { get; }
        float Spacing { get; }

        void SetVisibleChildren(List<IndexedLayoutData> visibleChildren);

        /// <summary>
        ///     Lazy mode only. Ensures indices in [startIndexInclusive, endIndexExclusive) are built (constructing
        ///     newly-entering ones and evicting ones that fell outside the window since the last call), and returns
        ///     their states in index order.
        /// </summary>
        IState[] RequestBuildWindow(int startIndexInclusive, int endIndexExclusive);
    }

    public class RenderSliverList : RenderObject, IMultiChildrenRenderObject
    {
        // Caches the measured sizes of all children to avoid re-calculating every frame. Eager mode only.
        private readonly List<Vector2> _allChildrenSizes = new();
        private readonly ISliverState _state;
        private readonly List<LayoutInfo> _visibleChildrenLayout = new();
        private readonly List<IndexedLayoutData> _visibleChildrenIndexed = new();
        private Vector2 _viewportSize; // this is determined after the sizing pass, based on the constraints from the parent.
        private float _virtualizationCacheExtent; // this is determined after the sizing pass, based on the view port size OR the user-defined value.
                                                  // (todo: probably not too much sense in having this an absolute value, maybe it should be a percentage)

        // --- Lazy mode (ItemBuilder/ItemCount) ---
        // Every index's measured main-axis extent, kept even after the item is evicted from the build window --
        // cheap (a float per index), and keeps TotalContentSize()/scroll-to math accurate for previously-seen
        // items instead of degrading to the coarse average the moment they scroll off.
        private readonly Dictionary<int, float> _measuredExtents = new();
        private const float DefaultEstimatedExtent = 100f; // fallback before anything has ever been measured

        // How strongly each pass's window average pulls _averageExtent toward it: 0 = never changes, 1 =
        // snaps straight to whatever this pass's (small) window happens to contain. See _averageExtent doc.
        private const float AverageExtentSmoothing = 0.25f;

        // The estimate for items that have NEVER been measured. An exponential moving average of each pass's
        // built-window average, NOT a cumulative average since app start and NOT a straight snap to the
        // current pass either -- mirrors the spirit of Flutter's default sliver extrapolation
        // (_extrapolateMaxScrollOffset in widgets/sliver.dart), which always recomputes its average fresh
        // from only the currently reified children, but damped here because our build window is much smaller
        // and gets rebuilt from scratch every pass (Flutter's reified range instead grows incrementally and
        // is typically much larger), so a straight snap swings hard on every pass whenever the window happens
        // to catch or miss a heterogeneous outlier. A cumulative average, on the other hand, lets one outlier
        // (e.g. an expanded header row) permanently skew the estimate for the rest of the session, even long
        // after scrolling away from it, because a bounded build window may never revisit enough "normal"
        // items to dilute it back out. The EMA sits between those two failure modes: it fully forgets a
        // stale outlier within a few passes, without swinging to it (or away from it) in a single pass.
        private float _averageExtent = DefaultEstimatedExtent;

        // The exact window built+measured by the last PerformSizing() pass, reused by PerformPositioning()
        // so it doesn't need to re-estimate or re-invoke RequestBuildWindow (which would re-run ItemBuilder).
        // _lazyWindowScrollOffset is the scroll offset the window was *selected* for; PerformLazyPositioning
        // culls against it (not the live offset) so the viewport it trims to matches the one the window was
        // built to cover. The window's absolute on-screen anchor is deliberately NOT cached here -- it's
        // re-derived in PerformLazyPositioning from the same measured-extent model as TotalContentSize()
        // (see EstimateLeadingEdgeOffset). That shared model is what keeps the positioned content's trailing
        // edge aligned with the content size the view sizes its content rect to; a uniform-stride anchor
        // drifts from that size by the accumulated (average - measured) error of the items before the window,
        // which at the bottom pushes the final item past the scrollable range and clips it.
        private int _lazyWindowStart;
        private float _lazyWindowScrollOffset;
        private readonly List<Vector2> _lazyWindowSizes = new();

        public RenderSliverList(ISliverState state) : base(state.StateLifetime)
        {
            _state = state;
        }

        private float ComputeVirtualizationCacheExtent()
        {
            var isHorizontal = this._state.Axis == Axis.Horizontal;
            var viewportSize = isHorizontal ? this._viewportSize.x : this._viewportSize.y;

            // Default to 1x the viewport size if not set.
            // Note: This means that we have 'three' screens worth of cache:
            // - 1 screen before the viewport
            // - 1 screen in the viewport
            // - 1 screen after the viewport
            return viewportSize;
        }

        public IReadOnlyList<LayoutInfo> ChildrenLayout => _visibleChildrenLayout;

        /// <summary>
        ///     SIZING PASS: measures children to determine the total scrollable content size. In eager mode,
        ///     every child is measured every pass. In lazy mode, only an estimated build window (viewport +
        ///     cache extent) is built and measured -- see <see cref="PerformLazySizing"/>.
        /// </summary>
        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            var isHorizontal = _state.Axis == Axis.Horizontal;
            var isVertical = !isHorizontal;

            if (isHorizontal && !constraints.HasBoundedWidth)
                throw new InvalidOperationException(
                    "A horizontal ScrollingList must have a bounded width." +
                    "This usually means it is being placed inside another horizontal scrollable or a Row without an Expanded widget."
                );

            if (isVertical && !constraints.HasBoundedHeight)
                throw new InvalidOperationException(
                    "A vertical ScrollingList must have a bounded height." +
                    "This usually means it is being placed inside another vertical scrollable or a Column without an Expanded widget."
                );

            // The RenderObject's own size is simply the size of the viewport, as dictated by the parent's
            // constraints -- computed up front since the lazy path needs it (for its build-window estimate)
            // before any children are measured.
            this._viewportSize = constraints.Largest;
            if (_state.VirtualizationCacheExtent.HasValue && _state.VirtualizationCacheExtent.Value < 0)
                throw new InvalidOperationException("VirtualizationCacheExtent cannot be negative.");
            this._virtualizationCacheExtent = _state.VirtualizationCacheExtent ?? ComputeVirtualizationCacheExtent();

            if (_state.ItemCount.HasValue)
                PerformLazySizing(constraints, isHorizontal, isVertical);
            else
                PerformEagerSizing(constraints, isHorizontal, isVertical);

            return this._viewportSize;
        }

        private void PerformEagerSizing(LayoutConstraints constraints, bool isHorizontal, bool isVertical)
        {
            _allChildrenSizes.Clear();

            // Give children unconstrained space along the main scrolling axis
            // but constrain them to the viewport's size on the cross axis. (stretching them horizontally)
            var childConstraints = MakeChildConstraints(constraints, isHorizontal, mainAxisExtent: null);

            foreach (var child in _state.AllChildren)
            {
                var childSize = LayoutChild(child, childConstraints);
                ThrowIfUnconstrainedMainAxis(childSize, isHorizontal, isVertical);
                _allChildrenSizes.Add(childSize);
            }
        }

        /// <summary>
        ///     Estimates which index range covers [scrollOffset - cacheExtent, scrollOffset + viewport + cacheExtent]
        ///     using a uniform per-item stride (the fixed ItemExtent if given, else the average extent measured
        ///     in the most recently built window), builds exactly that window, and measures it. The uniform-stride
        ///     estimate is an approximation for indices that haven't been measured yet: right after a big jump
        ///     (ScrollTo into unbuilt territory, or a fast scroll) it may not fully cover the real viewport on the
        ///     first pass, but it self-corrects on the next layout pass as the average refines near the new
        ///     position. This is the same class of approximation Flutter's own SliverList makes; accepting one
        ///     settle-frame of imprecision is what keeps this a single synchronous pass instead of an iterative
        ///     re-entrant layout algorithm.
        /// </summary>
        private void PerformLazySizing(LayoutConstraints constraints, bool isHorizontal, bool isVertical)
        {
            var itemCount = _state.ItemCount!.Value;
            _lazyWindowSizes.Clear();

            if (itemCount <= 0)
            {
                _state.RequestBuildWindow(0, 0);
                _lazyWindowStart = 0;
                return;
            }

            var itemExtent = _state.ItemExtent;
            var spacing = _state.Spacing;
            var cacheExtent = this._virtualizationCacheExtent;
            var viewportMainAxisSize = isHorizontal ? this._viewportSize.x : this._viewportSize.y;

            var stride = (itemExtent ?? _averageExtent) + spacing;
            var scrollOffset = _state.ScrollPixelOffset;

            var startIndex = Mathf.Clamp(Mathf.FloorToInt((scrollOffset - cacheExtent) / stride), 0, itemCount - 1);
            var endIndex = Mathf.Clamp(Mathf.CeilToInt((scrollOffset + viewportMainAxisSize + cacheExtent) / stride),
                startIndex + 1, itemCount);

            var childConstraints = MakeChildConstraints(constraints, isHorizontal, mainAxisExtent: itemExtent);
            var builtStates = _state.RequestBuildWindow(startIndex, endIndex);

            var hadPriorMeasurement = _measuredExtents.Count > 0;
            var measuredSumThisPass = 0f;

            for (var i = 0; i < builtStates.Length; i++)
            {
                var index = startIndex + i;
                var childSize = LayoutChild(builtStates[i], childConstraints);
                ThrowIfUnconstrainedMainAxis(childSize, isHorizontal, isVertical);

                var extent = isHorizontal ? childSize.x : childSize.y;
                _measuredExtents[index] = extent;
                _lazyWindowSizes.Add(childSize);
                measuredSumThisPass += extent;
            }

            // Nudge the estimate toward this pass's window average rather than snapping to it -- snapping
            // made the estimate swing hard whenever the (small, ~10-15 item) window happened to catch or miss
            // an outlier between consecutive passes, causing visible jumps in item positions/scrollbar size.
            // An EMA damps that noise while still fully forgetting stale outliers within a few passes, unlike
            // a cumulative average (see the _averageExtent field doc). Skip the damping for the very first
            // real measurement, since there's nothing meaningful to blend the arbitrary default extent with.
            if (_lazyWindowSizes.Count > 0)
            {
                var passAverage = measuredSumThisPass / _lazyWindowSizes.Count;
                _averageExtent = hadPriorMeasurement
                    ? Mathf.Lerp(_averageExtent, passAverage, AverageExtentSmoothing)
                    : passAverage;
            }

            _lazyWindowStart = startIndex;
            _lazyWindowScrollOffset = scrollOffset;
        }

        private static LayoutConstraints MakeChildConstraints(LayoutConstraints constraints, bool isHorizontal, float? mainAxisExtent)
        {
            var mainAxisMax = mainAxisExtent ?? float.PositiveInfinity;
            var mainAxisMin = mainAxisExtent ?? 0f;

            return isHorizontal
                // For a horizontal list, height is tight, width is loose (or tight to ItemExtent, if given).
                ? new LayoutConstraints(mainAxisMin, constraints.MaxHeight, mainAxisMax, constraints.MaxHeight)
                // For a vertical list, width is tight, height is loose (or tight to ItemExtent, if given).
                : new LayoutConstraints(constraints.MaxWidth, mainAxisMin, constraints.MaxWidth, mainAxisMax);
        }

        private static void ThrowIfUnconstrainedMainAxis(Vector2 childSize, bool isHorizontal, bool isVertical)
        {
            if (childSize.x == float.PositiveInfinity && isHorizontal)
                throw new InvalidOperationException(
                    "Child of a horizontal ScrollingList cannot have an unconstrained width." +
                    "Make sure the child is not trying to expand infinitely (e.g. by being inside a Row without an Expanded)."
                );
            if (childSize.y == float.PositiveInfinity && isVertical)
                throw new InvalidOperationException(
                    "Child of a vertical ScrollingList cannot have an unconstrained height." +
                    "Make sure the child is not trying to expand infinitely (e.g. by being inside a Column without an Expanded)."
                );
        }

        public float TotalContentSize()
        {
            return _state.ItemCount.HasValue
                ? EstimateLazyContentMainAxisSize() + (_state.VirtualizationCacheExtent ?? 0)
                : EagerContentMainAxisSize() + (_state.VirtualizationCacheExtent ?? 0);
        }

        private float EagerContentMainAxisSize()
        {
            var isHorizontal = _state.Axis == Axis.Horizontal;
            float totalSize = 0;
            foreach (var childSize in _allChildrenSizes)
            {
                totalSize += isHorizontal ? childSize.x : childSize.y;
            }
            if (_allChildrenSizes.Count > 0)
                totalSize += _state.Spacing * (_allChildrenSizes.Count - 1);
            return totalSize;
        }

        /// <summary>
        ///     Sum of every exactly-measured extent (see <see cref="_measuredExtents"/>) plus the local window
        ///     average (see <see cref="_averageExtent"/>) extrapolated across every item never measured. No
        ///     cache-extent padding (see <see cref="TotalContentSize"/> for that) -- used directly by
        ///     scroll-to-index math, which shouldn't be skewed by that slack.
        /// </summary>
        private float EstimateLazyContentMainAxisSize()
        {
            var itemCount = _state.ItemCount!.Value;
            if (itemCount <= 0) return 0;

            float totalSize;
            if (_state.ItemExtent.HasValue)
            {
                totalSize = itemCount * _state.ItemExtent.Value;
            }
            else
            {
                var measuredSum = 0f;
                foreach (var extent in _measuredExtents.Values) measuredSum += extent;
                totalSize = measuredSum + _averageExtent * (itemCount - _measuredExtents.Count);
            }

            return totalSize + _state.Spacing * Mathf.Max(0, itemCount - 1);
        }

        /// <summary>
        ///     Pixel offset of item <paramref name="index"/>'s leading edge along the scrolling axis, built from
        ///     the SAME model as <see cref="EstimateLazyContentMainAxisSize"/>: the exact measured extent of
        ///     every already-measured item before <paramref name="index"/> (see <see cref="_measuredExtents"/>),
        ///     and the running <see cref="_averageExtent"/> for the rest. Anchoring the built window (and
        ///     scroll-to math) here rather than at a uniform <c>index * stride</c> is what keeps positioning
        ///     consistent with the content size the view is sized to: with a uniform anchor, items already
        ///     measured to sizes that differ from the running average drift the window off by their accumulated
        ///     error, which at the end of the list pushes the final item past the scrollable range (clipping it)
        ///     or short of it (leaving a gap).
        /// </summary>
        private float EstimateLeadingEdgeOffset(int index)
        {
            var spacing = _state.Spacing;

            if (_state.ItemExtent.HasValue)
                return index * (_state.ItemExtent.Value + spacing);

            var measuredSumBelow = 0f;
            var measuredCountBelow = 0;
            foreach (var pair in _measuredExtents)
            {
                if (pair.Key >= index) continue;
                measuredSumBelow += pair.Value;
                measuredCountBelow++;
            }

            return measuredSumBelow + _averageExtent * (index - measuredCountBelow) + spacing * index;
        }

        /// <summary>
        ///     POSITIONING PASS: calculates positions for ONLY the visible children.
        /// </summary>
        protected override void PerformPositioning()
        {
            var visibleChildrenData = _visibleChildrenIndexed;
            visibleChildrenData.Clear();

            if (_state.ItemCount.HasValue)
                PerformLazyPositioning(visibleChildrenData);
            else
                PerformEagerPositioning(visibleChildrenData);

            _visibleChildrenLayout.Clear();
            foreach (var visibleChild in visibleChildrenData) _visibleChildrenLayout.Add(visibleChild.Layout);

            // Push the results back to the state.
            _state.SetVisibleChildren(visibleChildrenData);
        }

        private void PerformEagerPositioning(List<IndexedLayoutData> visibleChildrenData)
        {
            var cacheExtent = this._virtualizationCacheExtent; // The buffer for smooth scrolling.

            var isHorizontal = _state.Axis == Axis.Horizontal;
            var scrollOffset = _state.ScrollPixelOffset;
            var spacing = _state.Spacing;
            var viewportMainAxisSize = isHorizontal ? this._viewportSize.x : this._viewportSize.y;

            var viewportStart = scrollOffset - cacheExtent;
            var viewportEnd = scrollOffset + viewportMainAxisSize + cacheExtent;

            float mainAxisPos = 0;
            for (var i = 0; i < _allChildrenSizes.Count; i++)
            {
                var childSize = _allChildrenSizes[i];
                var childMainAxisSize = isHorizontal ? childSize.x : childSize.y;

                var childStart = mainAxisPos;
                var childEnd = childStart + childMainAxisSize;

                // The core culling logic: if the child intersects the viewport (plus buffer), it's visible.
                if (childEnd > viewportStart && childStart < viewportEnd)
                {
                    // Calculate position relative to the viewport's top-left corner.
                    var cornerPosition = isHorizontal
                        ? new Vector2(mainAxisPos, 0)
                        : new Vector2(0, mainAxisPos);

                    visibleChildrenData.Add(new IndexedLayoutData
                    {
                        ChildIndex = i,
                        Layout = new LayoutInfo
                        {
                            Size = childSize,
                            Position = cornerPosition
                        }
                    });
                }

                mainAxisPos += childMainAxisSize + spacing;
            }
        }

        /// <summary>
        ///     Walks just the window PerformLazySizing already built and measured this pass. The window's
        ///     leading edge is anchored via <see cref="EstimateLeadingEdgeOffset"/> -- the same measured-extent
        ///     model <see cref="EstimateLazyContentMainAxisSize"/> uses -- so the positioned content ends
        ///     exactly where the view sizes its content rect, and the final item can't be pushed past the
        ///     scrollable range and clipped. Sizes and spacing *within* the window are exact; only the leading
        ///     edge (items before the window that haven't been measured) is an estimate. Culling uses
        ///     _lazyWindowScrollOffset -- the offset the window was selected for -- not the live scroll offset.
        /// </summary>
        private void PerformLazyPositioning(List<IndexedLayoutData> visibleChildrenData)
        {
            if (_lazyWindowSizes.Count == 0) return;

            var isHorizontal = _state.Axis == Axis.Horizontal;
            var cacheExtent = this._virtualizationCacheExtent;
            var scrollOffset = _lazyWindowScrollOffset;
            var spacing = _state.Spacing;
            var viewportMainAxisSize = isHorizontal ? this._viewportSize.x : this._viewportSize.y;

            var viewportStart = scrollOffset - cacheExtent;
            var viewportEnd = scrollOffset + viewportMainAxisSize + cacheExtent;

            var mainAxisPos = EstimateLeadingEdgeOffset(_lazyWindowStart);

            for (var i = 0; i < _lazyWindowSizes.Count; i++)
            {
                var index = _lazyWindowStart + i;
                var childSize = _lazyWindowSizes[i];
                var childMainAxisSize = isHorizontal ? childSize.x : childSize.y;

                var childStart = mainAxisPos;
                var childEnd = childStart + childMainAxisSize;

                if (childEnd > viewportStart && childStart < viewportEnd)
                {
                    var cornerPosition = isHorizontal
                        ? new Vector2(mainAxisPos, 0)
                        : new Vector2(0, mainAxisPos);

                    visibleChildrenData.Add(new IndexedLayoutData
                    {
                        ChildIndex = index,
                        Layout = new LayoutInfo
                        {
                            Size = childSize,
                            Position = cornerPosition
                        }
                    });
                }

                mainAxisPos += childMainAxisSize + spacing;
            }
        }

        // Intrinsic sizing is used to determine the total size of the scrollable content. In lazy mode this
        // returns the same estimate TotalContentSize() is based on, rather than forcing a full eager measurement
        // of every item -- which would defeat the point of laziness the moment any ancestor queries it.
        protected override float ComputeIntrinsicHeight(float width)
        {
            if (_state.Axis == Axis.Horizontal) return 0; // Not meaningful for a horizontal list
            if (_state.ItemCount.HasValue) return EstimateLazyContentMainAxisSize();

            float totalHeight = 0;
            foreach (var child in _state.AllChildren)
            {
                totalHeight += GetChildIntrinsicHeight(child, width);
            }

            if (_state.AllChildren.Length > 0)
                totalHeight += _state.Spacing * (_state.AllChildren.Length - 1);


            return totalHeight;
        }

        protected override float ComputeIntrinsicWidth(float height)
        {
            if (_state.Axis == Axis.Vertical) return 0; // Not meaningful for a vertical list
            if (_state.ItemCount.HasValue) return EstimateLazyContentMainAxisSize();

            float totalWidth = 0;
            foreach (var child in _state.AllChildren)
            {
                totalWidth += GetChildIntrinsicWidth(child, height);
            }

            if (_state.AllChildren.Length > 0)
                totalWidth += _state.Spacing * (_state.AllChildren.Length - 1);

            return totalWidth;
        }



        private static float GetChildIntrinsicWidth(IState child, float height)
        {
            return child.RenderObject.GetIntrinsicWidth(height);
        }

        private static float GetChildIntrinsicHeight(IState child, float width)
        {
            return child.RenderObject.GetIntrinsicHeight(width);
        }

        /// <summary>
        ///     Target scroll offset in pixels to bring <paramref name="index"/> into view at <paramref name="position"/>.
        ///     Returns pixels (not a 0..1 ratio) for the same reason <see cref="ISliverState.ScrollPixelOffset"/> is
        ///     pixel-based -- see that property's doc.
        /// </summary>
        public float CalculateScrollPixelOffset(int index, ScrollToPosition position)
        {
            return _state.ItemCount.HasValue
                ? CalculateLazyScrollPixelOffset(index, position)
                : CalculateEagerScrollPixelOffset(index, position);
        }

        private float CalculateEagerScrollPixelOffset(int index, ScrollToPosition position)
        {
            var isHorizontal = _state.Axis == Axis.Horizontal;
            var viewportSize = isHorizontal ? this._viewportSize.x : this._viewportSize.y;


            float totalContentSize = 0;
            var mainAxisKey = isHorizontal ? 0 : 1; // 0 for x, 1 for y
            for (var i = 0; i < _allChildrenSizes.Count; i++)
            {
                totalContentSize += _allChildrenSizes[i][mainAxisKey];
            }
            // Add spacing
            if (_allChildrenSizes.Count > 0)
            {
                totalContentSize += _state.Spacing * (_allChildrenSizes.Count - 1);
            }

            var totalScrollableDist = totalContentSize - viewportSize;

            // If there's nothing to scroll (content fits in the viewport), the offset is always 0.
            if (totalScrollableDist <= 0 || index < 0 || index >= _allChildrenSizes.Count)
                return 0;

            // Get the pixel offset of the target child.
            float childOffset = 0;
            for (var i = 0; i < index; i++)
            {
                childOffset += (isHorizontal ? _allChildrenSizes[i].x : _allChildrenSizes[i].y) + _state.Spacing;
            }

            var childSize = isHorizontal ? _allChildrenSizes[index].x : _allChildrenSizes[index].y;

            switch (position)
            {
                case ScrollToPosition.Start:
                    // childOffset remains unchanged, already at the start position.
                    break;
                case ScrollToPosition.Center:
                    childOffset -= (viewportSize - childSize) / 2;
                    break;
                case ScrollToPosition.End:
                    childOffset -= viewportSize - childSize;
                    break;
            }

            return Mathf.Clamp(childOffset, 0, totalScrollableDist);
        }

        /// <summary>
        ///     Same shape as <see cref="CalculateEagerScrollPixelOffset"/>. The target's leading edge comes from
        ///     <see cref="EstimateLeadingEdgeOffset"/> -- the same measured-extent model as
        ///     <see cref="PerformLazyPositioning"/> and <see cref="EstimateLazyContentMainAxisSize"/>, so a
        ///     scroll-to lands where the item is actually positioned -- and its size is the exact measured extent
        ///     when the item has ever been measured (see <see cref="_measuredExtents"/>), the running average
        ///     otherwise. Scrolling to an index in a region that hasn't been measured yet may land imprecisely on
        ///     the first pass and settle once it's built.
        /// </summary>
        private float CalculateLazyScrollPixelOffset(int index, ScrollToPosition position)
        {
            var itemCount = _state.ItemCount!.Value;
            var isHorizontal = _state.Axis == Axis.Horizontal;
            var viewportSize = isHorizontal ? this._viewportSize.x : this._viewportSize.y;

            var totalContentSize = EstimateLazyContentMainAxisSize();
            var totalScrollableDist = totalContentSize - viewportSize;

            if (totalScrollableDist <= 0 || index < 0 || index >= itemCount)
                return 0;

            var childOffset = EstimateLeadingEdgeOffset(index);
            var childSize = _state.ItemExtent.HasValue
                ? _state.ItemExtent.Value
                : _measuredExtents.TryGetValue(index, out var exact)
                    ? exact
                    : _averageExtent;

            switch (position)
            {
                case ScrollToPosition.Start:
                    break;
                case ScrollToPosition.Center:
                    childOffset -= (viewportSize - childSize) / 2;
                    break;
                case ScrollToPosition.End:
                    childOffset -= viewportSize - childSize;
                    break;
            }

            return Mathf.Clamp(childOffset, 0, totalScrollableDist);
        }
    }
}
