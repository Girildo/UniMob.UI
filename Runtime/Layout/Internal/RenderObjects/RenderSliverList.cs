using System;
using System.Collections.Generic;
using UniMob.UI.Layout.Internal.Views;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    // ================================================================================================
    //  Virtualized scrolling list -- architecture & pitfalls (read this before editing the lazy math)
    // ================================================================================================
    //
    //  Four cooperating pieces:
    //    * ScrollList (widget)     public config: eager `Children` XOR lazy `ItemBuilder`+`ItemCount`, an
    //                              optional fixed `ItemExtent`, axis, spacing, cache extent.
    //    * ScrollListState         bridges the imperative layout pass to the reactive UI: owns the lazily-built
    //                              item States and exposes only the *visible* subset as an [Atom] for the View,
    //                              while handing this render object the *full* logical shape (count / extent /
    //                              scroll offset). Its own hazards are documented in ScrollList.cs.
    //    * RenderSliverList (here) the geometry engine. Two passes per layout: PerformSizing (measure + compute
    //                              total content size; in lazy mode also pick & build the window) then
    //                              PerformPositioning (place + cull only the children intersecting the viewport).
    //    * ScrollListView          the Unity ScrollRect adapter: sizes the content rect to TotalContentSize()
    //                              and converts ScrollRect position <-> ScrollController.PixelOffset.
    //
    //  Eager mode (ItemCount == null): every child is measured every pass and positions are exact. Simple.
    //
    //  Lazy mode (ItemCount set): only a window covering [viewport +/- cacheExtent] is built and measured each
    //  pass; everything outside it is *estimated*. Two ideas carry it -- and both historical bugs lived here:
    //
    //    (1) One model for "where is item N". _measuredExtents holds the exact extent of every item ever
    //        measured (kept even after eviction); _averageExtent is an EMA estimate for the never-measured rest.
    //        EstimateLeadingEdgeOffset(index) walks that model to give an item's leading edge, and positioning's
    //        window anchor, TotalContentSize(), AND scroll-to all route through it. This is load-bearing: if any
    //        one of them computes offsets differently (e.g. a uniform index*stride), it drifts from the others by
    //        the accumulated (average - measured) error of the items before it, and at the end of the list that
    //        pushes the final item past the scrollable range -> the View's RectMask2D clips it (opposite sign: a
    //        gap). Route every offset through the one function.
    //
    //    (2) The one-settle-frame approximation. Window *selection* (which indices to build) uses a cheap uniform
    //        stride guess from _averageExtent. After a big jump (ScrollTo into unbuilt territory, a fast fling) it
    //        can miss the true viewport on the first pass, then self-corrects next pass as the average refines
    //        near the new position; the cache buffer absorbs the slop. Accepting that one frame is what keeps
    //        layout a single synchronous pass instead of an iterative re-entrant one.
    //
    //  Coordinate space: ScrollController.PixelOffset is absolute pixels (not a 0..1 ratio) because the total-size
    //  estimate shifts almost every pass and an absolute offset doesn't drift under it. Since the View sizes the
    //  content rect to TotalContentSize(), max scroll == TotalContentSize() - viewport, so positioned content
    //  MUST end exactly there -- which is precisely why pitfall (1) bites.
    //
    //  When editing, keep in mind:
    //    * _averageExtent is mutated mid-pass (during sizing); positioning and TotalContentSize() both read it
    //      afterwards so they agree within a pass -- don't feed one a pre-update copy and the other a post-update
    //      one.
    //    * Lazy selection is only an estimate; never assume the built window exactly equals the visible set.
    //    * The reactive build/dispose vs ItemBuilder NoWatch rules live in ScrollList.cs -- see the notes there.
    // ================================================================================================

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

    public class RenderSliverList : RenderObject, IScrollableRenderObject
    {
        // Caches the measured sizes of all children to avoid re-calculating every frame. Eager mode only.
        private readonly List<Vector2> _allChildrenSizes = new();
        private readonly ISliverState _state;
        private readonly List<LayoutInfo> _visibleChildrenLayout = new();
        private readonly List<IndexedLayoutData> _visibleChildrenIndexed = new();
        private Vector2 _viewportSize; // this is determined after the sizing pass, based on the constraints from the parent.
        private float _virtualizationCacheExtent; // this is determined after the sizing pass, based on the view port size OR the user-defined value.
                                                  // (todo: probably not too much sense in having this an absolute value, maybe it should be a percentage)

        // --- Lazy mode (ItemBuilder/ItemCount) -- see the "one model for where is item N" note up top. ---
        // Exact main-axis extent of every item ever measured, kept even after it leaves the build window (cheap,
        // and keeps TotalContentSize()/scroll-to exact for seen items instead of falling back to the average).
        private readonly Dictionary<int, float> _measuredExtents = new();
        private const float DefaultEstimatedExtent = 100f; // fallback before anything has ever been measured

        // How strongly each pass's window average pulls _averageExtent toward it (0 = never, 1 = snap).
        private const float AverageExtentSmoothing = 0.25f;

        // Estimate for items never yet measured. An EMA of each pass's built-window average -- deliberately not a
        // cumulative average (one outlier like a tall header would skew it for the whole session, since a bounded
        // window may never revisit enough normal items to dilute it out) and not a straight snap (our small window
        // swings hard whenever it catches/misses an outlier between passes). The EMA forgets a stale outlier
        // within a few passes without lurching toward it in any single one.
        private float _averageExtent = DefaultEstimatedExtent;

        // The window built+measured by the last PerformSizing() pass, reused by PerformPositioning() (so it never
        // re-runs RequestBuildWindow / ItemBuilder). _lazyWindowScrollOffset is the offset the window was
        // *selected* for -- positioning culls against it, not the live offset. The window's on-screen anchor is
        // intentionally not cached: positioning re-derives it via EstimateLeadingEdgeOffset (pitfall (1) up top).
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

        public IReadOnlyList<LayoutInfo> ChildrenLayout
        {
            get
            {
                // Pulls layout before handing the list out. The list is only valid immediately
                // after a pass, so a caller that reads it without one stamps stale positions onto
                // live RectTransforms -- silently, and only while something else happens to move.
                WatchLayout();
                return _visibleChildrenLayout;
            }
        }

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
            var childConstraints = SliverLayoutMath.MakeChildConstraints(constraints, isHorizontal, mainAxisExtent: null);

            foreach (var child in _state.AllChildren)
            {
                var childSize = LayoutChild(child, childConstraints);
                SliverLayoutMath.ThrowIfUnconstrainedMainAxis(childSize, isHorizontal, isVertical);
                _allChildrenSizes.Add(childSize);
            }
        }

        /// <summary>
        ///     Picks the index range covering [scrollOffset - cache, scrollOffset + viewport + cache] via a uniform
        ///     stride guess (ItemExtent if fixed, else <see cref="_averageExtent"/>), then builds and measures
        ///     exactly that window. The guess is the one-settle-frame approximation described up top: it can
        ///     under-cover right after a big jump and self-corrects next pass as the average refines.
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

            var childConstraints = SliverLayoutMath.MakeChildConstraints(constraints, isHorizontal, mainAxisExtent: itemExtent);
            var builtStates = _state.RequestBuildWindow(startIndex, endIndex);

            var hadPriorMeasurement = _measuredExtents.Count > 0;
            var measuredSumThisPass = 0f;

            for (var i = 0; i < builtStates.Length; i++)
            {
                var index = startIndex + i;
                var childSize = LayoutChild(builtStates[i], childConstraints);
                SliverLayoutMath.ThrowIfUnconstrainedMainAxis(childSize, isHorizontal, isVertical);

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

        // The scrollable content size the View sizes its content rect to -- the size of the *content* only. The
        // virtualization cache extent must NOT be added here: it's an off-screen build/warm buffer, not scrollable
        // space. Folding it in inflated the content rect, leaving a dead-zone of empty scroll past the last item
        // whenever an explicit VirtualizationCacheExtent was set.
        public float TotalContentSize()
        {
            return _state.ItemCount.HasValue
                ? EstimateLazyContentMainAxisSize()
                : EagerContentMainAxisSize();
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
        ///     Total main-axis size of the lazy content. Derived from the exact same per-item model as positioning:
        ///     the content ends at the leading edge of the item one past the last, minus the trailing gap that edge
        ///     would add (the list has itemCount-1 gaps, not itemCount). Sharing <see cref="EstimateLeadingEdgeOffset"/>
        ///     is what keeps the scrollable range and the positioned content in lockstep.
        /// </summary>
        private float EstimateLazyContentMainAxisSize()
        {
            var itemCount = _state.ItemCount!.Value;
            if (itemCount <= 0) return 0;

            return EstimateLeadingEdgeOffset(itemCount) - _state.Spacing;
        }

        /// <summary>
        ///     THE single source of truth for lazy geometry (pitfall (1) up top): the pixel offset of item
        ///     <paramref name="index"/>'s leading edge, using exact measured extents where known (see
        ///     <see cref="_measuredExtents"/>) and the running <see cref="_averageExtent"/> otherwise. Positioning,
        ///     <see cref="EstimateLazyContentMainAxisSize"/>, and scroll-to all route through it so they can't drift.
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
        ///     POSITIONING PASS: positions and culls only the children intersecting the viewport. Eager mode
        ///     walks every measured child from offset 0; lazy mode walks just the window built this pass,
        ///     anchored via <see cref="EstimateLeadingEdgeOffset"/> so it stays in lockstep with
        ///     <see cref="TotalContentSize"/> (and the final item can't be pushed past the scrollable range and
        ///     clipped). Both share <see cref="CullVisibleRun"/>; only the run they hand it differs.
        /// </summary>
        protected override void PerformPositioning()
        {
            var visible = _visibleChildrenIndexed;
            visible.Clear();

            if (_state.ItemCount.HasValue)
            {
                // Lazy: only the window PerformLazySizing built this pass, anchored at its measured leading edge
                // and culled against the offset it was selected for (not the live one).
                if (_lazyWindowSizes.Count > 0)
                    CullVisibleRun(_lazyWindowStart, EstimateLeadingEdgeOffset(_lazyWindowStart),
                        _lazyWindowSizes, _lazyWindowScrollOffset, visible);
            }
            else
            {
                CullVisibleRun(0, 0f, _allChildrenSizes, _state.ScrollPixelOffset, visible);
            }

            _visibleChildrenLayout.Clear();
            foreach (var child in visible) _visibleChildrenLayout.Add(child.Layout);

            _state.SetVisibleChildren(visible);
        }

        /// <summary>
        ///     Positions a contiguous run of already-measured children -- the first at <paramref name="firstLeadingEdge"/>,
        ///     each subsequent one a spacing gap after the previous -- and keeps only those intersecting the
        ///     viewport plus the cache buffer, in index order. The eager and lazy passes differ only in the run
        ///     they supply (all children from offset 0, versus the built window from its measured anchor); the
        ///     culling and positioning are identical and live here so they can't drift apart.
        /// </summary>
        private void CullVisibleRun(int firstIndex, float firstLeadingEdge, List<Vector2> sizes,
            float scrollOffset, List<IndexedLayoutData> output)
        {
            var isHorizontal = _state.Axis == Axis.Horizontal;
            var spacing = _state.Spacing;
            var viewportMainAxisSize = isHorizontal ? _viewportSize.x : _viewportSize.y;

            var viewportStart = scrollOffset - _virtualizationCacheExtent;
            var viewportEnd = scrollOffset + viewportMainAxisSize + _virtualizationCacheExtent;

            var mainAxisPos = firstLeadingEdge;
            for (var i = 0; i < sizes.Count; i++)
            {
                var childSize = sizes[i];
                var childMainAxisSize = isHorizontal ? childSize.x : childSize.y;

                // Visible if it intersects the viewport (plus buffer) along the scrolling axis.
                if (mainAxisPos + childMainAxisSize > viewportStart && mainAxisPos < viewportEnd)
                {
                    output.Add(new IndexedLayoutData
                    {
                        ChildIndex = firstIndex + i,
                        Layout = new LayoutInfo
                        {
                            Size = childSize,
                            Position = isHorizontal ? new Vector2(mainAxisPos, 0) : new Vector2(0, mainAxisPos),
                        },
                    });
                }

                mainAxisPos += childMainAxisSize + spacing;
            }
        }

        // Intrinsic sizing reports the total main-axis size of the scrollable content. A list has no meaningful
        // intrinsic size on its cross axis (it stretches to the constraint), so that axis returns 0. In lazy mode
        // this returns the same estimate TotalContentSize() is based on rather than forcing a full measurement of
        // every item -- which would defeat laziness the moment any ancestor queries it.
        protected override float ComputeIntrinsicHeight(float width)
            => _state.Axis == Axis.Horizontal ? 0 : ComputeIntrinsicMainAxisSize(width);

        protected override float ComputeIntrinsicWidth(float height)
            => _state.Axis == Axis.Vertical ? 0 : ComputeIntrinsicMainAxisSize(height);

        private float ComputeIntrinsicMainAxisSize(float crossAxisExtent)
        {
            if (_state.ItemCount.HasValue) return EstimateLazyContentMainAxisSize();

            var isHorizontal = _state.Axis == Axis.Horizontal;
            var children = _state.AllChildren;

            var total = 0f;
            foreach (var child in children)
                total += isHorizontal
                    ? child.RenderObject.GetIntrinsicWidth(crossAxisExtent)
                    : child.RenderObject.GetIntrinsicHeight(crossAxisExtent);

            if (children.Length > 0)
                total += _state.Spacing * (children.Length - 1);

            return total;
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
            var viewportSize = isHorizontal ? _viewportSize.x : _viewportSize.y;

            var totalScrollableDist = EagerContentMainAxisSize() - viewportSize;

            // If there's nothing to scroll (content fits in the viewport), the offset is always 0.
            if (totalScrollableDist <= 0 || index < 0 || index >= _allChildrenSizes.Count)
                return 0;

            var childOffset = 0f;
            for (var i = 0; i < index; i++)
                childOffset += (isHorizontal ? _allChildrenSizes[i].x : _allChildrenSizes[i].y) + _state.Spacing;

            var childSize = isHorizontal ? _allChildrenSizes[index].x : _allChildrenSizes[index].y;

            childOffset = SliverLayoutMath.AlignToScrollPosition(childOffset, childSize, viewportSize, position);
            return Mathf.Clamp(childOffset, 0, totalScrollableDist);
        }

        /// <summary>
        ///     Same shape as <see cref="CalculateEagerScrollPixelOffset"/>. The target's leading edge comes from
        ///     <see cref="EstimateLeadingEdgeOffset"/> -- the same measured-extent model as positioning and
        ///     <see cref="EstimateLazyContentMainAxisSize"/>, so a scroll-to lands where the item is actually
        ///     positioned -- and its size is the exact measured extent when the item has ever been measured (see
        ///     <see cref="_measuredExtents"/>), the running average otherwise. Scrolling to an index in a region
        ///     that hasn't been measured yet may land imprecisely on the first pass and settle once it's built.
        /// </summary>
        private float CalculateLazyScrollPixelOffset(int index, ScrollToPosition position)
        {
            var itemCount = _state.ItemCount!.Value;
            var isHorizontal = _state.Axis == Axis.Horizontal;
            var viewportSize = isHorizontal ? _viewportSize.x : _viewportSize.y;

            var totalScrollableDist = EstimateLazyContentMainAxisSize() - viewportSize;
            if (totalScrollableDist <= 0 || index < 0 || index >= itemCount)
                return 0;

            var childOffset = EstimateLeadingEdgeOffset(index);
            var childSize = _state.ItemExtent.HasValue
                ? _state.ItemExtent.Value
                : _measuredExtents.TryGetValue(index, out var exact)
                    ? exact
                    : _averageExtent;

            childOffset = SliverLayoutMath.AlignToScrollPosition(childOffset, childSize, viewportSize, position);
            return Mathf.Clamp(childOffset, 0, totalScrollableDist);
        }
    }
}
