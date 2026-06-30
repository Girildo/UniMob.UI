using System;
using System.Collections.Generic;
using UniMob.UI.Layout.Internal.Views;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    // A simple struct to pair a child's original index with its final layout data.
    internal struct IndexedLayoutData
    {
        public int ChildIndex;
        public LayoutInfo Layout;
    }

    internal interface ISliverState : IMultiChildLayoutState
    {
        IState[] AllChildren { get; }
        Axis Axis { get; }
        //Vector2 ViewportSize { get; }
        float NormalizedScrollOffset { get; }
        float? VirtualizationCacheExtent { get; }
        float Spacing { get; }

        internal void SetVisibleChildren(List<IndexedLayoutData> visibleChildren);
    }

    internal class RenderSliverList : RenderObject, IMultiChildrenRenderObject
    {
        // Caches the measured sizes of all children to avoid re-calculating every frame.
        private readonly List<Vector2> _allChildrenSizes = new();
        private readonly ISliverState _state;
        private readonly List<LayoutInfo> _visibleChildrenLayout = new();
        private readonly List<IndexedLayoutData> _visibleChildrenIndexed = new();
        private Vector2 _viewportSize; // this is determined after the sizing pass, based on the constraints from the parent.
        private float _virtualizationCacheExtent; // this is determined after the sizing pass, based on the view port size OR the user-defined value.
                                                  // (todo: probably not too much sense in having this an absolute value, maybe it should be a percentage)

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
        ///     SIZING PASS: Measures every child to determine the total scrollable content size.
        /// </summary>
        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            _allChildrenSizes.Clear();
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

            // Give children unconstrained space along the main scrolling axis
            // but constrain them to the viewport's size on the cross axis. (stretching them horizontally)
            LayoutConstraints childConstraints;
            if (isHorizontal)
                // For a horizontal list, height is tight, width is loose.
                childConstraints = new LayoutConstraints(0, constraints.MaxHeight, float.PositiveInfinity,
                    constraints.MaxHeight);
            else
                // For a vertical list, width is tight, height is loose.
                childConstraints = new LayoutConstraints(constraints.MaxWidth, 0, constraints.MaxWidth,
                    float.PositiveInfinity);



            foreach (var child in _state.AllChildren)
            {
                var childSize = LayoutChild(child, childConstraints);
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
                _allChildrenSizes.Add(childSize);
            }

            // The RenderObject's own size is simply the size of the viewport,
            // as dictated by the parent's constraints.
            //return new Vector2(constraints.MaxWidth, constraints.MaxHeight);
            this._viewportSize = constraints.Largest;
            if (_state.VirtualizationCacheExtent.HasValue && _state.VirtualizationCacheExtent.Value < 0)
                throw new InvalidOperationException("VirtualizationCacheExtent cannot be negative.");
            this._virtualizationCacheExtent = _state.VirtualizationCacheExtent ?? ComputeVirtualizationCacheExtent();
            return this._viewportSize;
        }

        public float TotalContentSize()
        {
            var isHorizontal = _state.Axis == Axis.Horizontal;
            float totalSize = 0;
            foreach (var childSize in _allChildrenSizes)
            {
                totalSize += isHorizontal ? childSize.x : childSize.y;
            }
            if (_allChildrenSizes.Count > 0)
                totalSize += _state.Spacing * (_allChildrenSizes.Count - 1);
            return totalSize + (_state.VirtualizationCacheExtent ?? 0);
        }

        /// <summary>
        ///     POSITIONING PASS: Calculates positions for ONLY the visible children.
        /// </summary>
        protected override void PerformPositioning()
        {
            var visibleChildrenData = _visibleChildrenIndexed;
            visibleChildrenData.Clear();
            var cacheExtent = this._virtualizationCacheExtent; // The buffer for smooth scrolling.

            var isHorizontal = _state.Axis == Axis.Horizontal;
            var scrollOffset = _state.NormalizedScrollOffset * this.TotalContentSize();
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

            _visibleChildrenLayout.Clear();
            foreach (var visibleChild in visibleChildrenData) _visibleChildrenLayout.Add(visibleChild.Layout);

            // Push the results back to the state.
            _state.SetVisibleChildren(visibleChildrenData);
        }

        // Intrinsic sizing is used to determine the total size of the scrollable content.
        protected override float ComputeIntrinsicHeight(float width)
        {
            if (_state.Axis == Axis.Horizontal) return 0; // Not meaningful for a horizontal list

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

        public float CalculateNormalizedOffset(int index, ScrollToPosition position)
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

            // Convert the pixel offset to a normalized value based on the SCROLLABLE distance.
            return childOffset / totalScrollableDist;
        }
    }
}