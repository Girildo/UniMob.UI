using System;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    /// <summary>
    ///     Axis-agnostic helpers shared by the scrollable sliver render objects
    ///     (<see cref="RenderSliverList" /> and <see cref="RenderSliverGrid" />). Extracted so the
    ///     child-constraint construction, the unbounded-main-axis guard, and the scroll-to alignment
    ///     math have exactly one implementation both must agree on.
    /// </summary>
    internal static class SliverLayoutMath
    {
        /// <summary>
        ///     Builds the constraints for a child of a scrollable sliver: tight on the cross axis (children
        ///     stretch to fill the viewport across), and on the main axis either tight to
        ///     <paramref name="mainAxisExtent" /> (a fixed item/cell size) or loose (measured).
        /// </summary>
        public static LayoutConstraints MakeChildConstraints(LayoutConstraints constraints, bool isHorizontal,
            float? mainAxisExtent)
        {
            var mainAxisMax = mainAxisExtent ?? float.PositiveInfinity;
            var mainAxisMin = mainAxisExtent ?? 0f;

            return isHorizontal
                // For a horizontal sliver, height is tight, width is loose (or tight to the extent, if given).
                ? new LayoutConstraints(mainAxisMin, constraints.MaxHeight, mainAxisMax, constraints.MaxHeight)
                // For a vertical sliver, width is tight, height is loose (or tight to the extent, if given).
                : new LayoutConstraints(constraints.MaxWidth, mainAxisMin, constraints.MaxWidth, mainAxisMax);
        }

        /// <summary>
        ///     Throws when a measured child reports an infinite size along the scrolling axis -- a child that
        ///     tries to expand infinitely inside a scrollable would have no meaningful extent to lay out.
        /// </summary>
        public static void ThrowIfUnconstrainedMainAxis(Vector2 childSize, bool isHorizontal, bool isVertical)
        {
            if (childSize.x == float.PositiveInfinity && isHorizontal)
                throw new InvalidOperationException(
                    "Child of a horizontal scrollable cannot have an unconstrained width." +
                    "Make sure the child is not trying to expand infinitely (e.g. by being inside a Row without an Expanded)."
                );
            if (childSize.y == float.PositiveInfinity && isVertical)
                throw new InvalidOperationException(
                    "Child of a vertical scrollable cannot have an unconstrained height." +
                    "Make sure the child is not trying to expand infinitely (e.g. by being inside a Column without an Expanded)."
                );
        }

        /// <summary>
        ///     Shifts a child's leading-edge offset to the requested spot in the viewport: Start keeps the
        ///     leading edge; Center/End pull it back by the appropriate slice of the leftover viewport space.
        /// </summary>
        public static float AlignToScrollPosition(float leadingEdge, float childSize, float viewportSize,
            ScrollToPosition position)
        {
            return position switch
            {
                ScrollToPosition.Center => leadingEdge - (viewportSize - childSize) / 2f,
                ScrollToPosition.End => leadingEdge - (viewportSize - childSize),
                _ => leadingEdge,
            };
        }
    }
}
