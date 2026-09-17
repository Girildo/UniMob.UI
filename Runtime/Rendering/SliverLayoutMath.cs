using System;
using UnityEngine;

namespace UniMob.UI.Rendering
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
        public static LayoutConstraints MakeChildConstraints(
            LayoutConstraints constraints,
            bool isHorizontal,
            float? mainAxisExtent
        )
        {
            var mainAxisMax = mainAxisExtent ?? float.PositiveInfinity;
            var mainAxisMin = mainAxisExtent ?? 0f;

            return isHorizontal
                // For a horizontal sliver, height is tight, width is loose (or tight to the extent, if given).
                ? new LayoutConstraints(
                    mainAxisMin,
                    constraints.MaxHeight,
                    mainAxisMax,
                    constraints.MaxHeight
                )
                // For a vertical sliver, width is tight, height is loose (or tight to the extent, if given).
                : new LayoutConstraints(
                    constraints.MaxWidth,
                    mainAxisMin,
                    constraints.MaxWidth,
                    mainAxisMax
                );
        }

        /// <summary>
        ///     Throws when a measured child reports an infinite size along the scrolling axis -- a child that
        ///     tries to expand infinitely inside a scrollable would have no meaningful extent to lay out.
        /// </summary>
        public static void ThrowIfUnconstrainedMainAxis(
            Vector2 childSize,
            bool isHorizontal,
            bool isVertical
        )
        {
            if (childSize.x == float.PositiveInfinity && isHorizontal)
                throw new InvalidOperationException(
                    "Child of a horizontal scrollable cannot have an unconstrained width."
                        + "Make sure the child is not trying to expand infinitely (e.g. by being inside a Row without an Expanded)."
                );
            if (childSize.y == float.PositiveInfinity && isVertical)
                throw new InvalidOperationException(
                    "Child of a vertical scrollable cannot have an unconstrained height."
                        + "Make sure the child is not trying to expand infinitely (e.g. by being inside a Column without an Expanded)."
                );
        }

        /// <summary>
        ///     The part of <paramref name="scrollOffset" /> the content can actually scroll to: never past
        ///     the point where its end meets the end of the viewport. A controller keeps the offset it was
        ///     given when the content shrinks, and a window built for that offset holds nothing to show.
        /// </summary>
        public static float OffsetWithinContent(
            float scrollOffset,
            float contentSize,
            float viewportSize
        ) => Mathf.Min(scrollOffset, Mathf.Max(0f, contentSize - viewportSize));

        /// <summary>The most rounds of <see cref="CorrectWindow" /> one sizing pass runs.</summary>
        public const int MaxWindowCorrections = 64;

        /// <summary>
        ///     The window of slots (list items, grid rows) to build next, given a built and measured
        ///     <paramref name="window" />; the same window when it already covers the viewport.
        ///     A window the model now places away from <paramref name="wanted" /> is replaced by it.
        ///     One that overlaps it grows on each side that leaves the viewport uncovered, toward
        ///     the cache range and by at most its own size, so a run of zero-extent slots costs a
        ///     logarithmic number of rounds and an unbounded cache extent does not overflow.
        /// </summary>
        /// <param name="window">The built slots, end exclusive.</param>
        /// <param name="windowStartEdge">Leading edge of the first built slot.</param>
        /// <param name="windowEndEdge">Trailing edge of the last built slot.</param>
        /// <param name="wanted">
        ///     The slots the current model selects for the viewport plus its cache extent.
        /// </param>
        /// <param name="coverEnd">End of the viewport plus its cache extent.</param>
        /// <param name="localStride">Average extent plus spacing of the slots in the window.</param>
        public static (int start, int end) CorrectWindow(
            (int start, int end) window,
            int slotCount,
            float windowStartEdge,
            float windowEndEdge,
            float viewportStart,
            float viewportEnd,
            (int start, int end) wanted,
            float coverEnd,
            float localStride
        )
        {
            var coversStart = window.start <= 0 || windowStartEdge <= viewportStart;
            var coversEnd = window.end >= slotCount || windowEndEdge >= viewportEnd;
            if (coversStart && coversEnd)
                return window;

            if (wanted.start >= window.end || wanted.end <= window.start)
                return wanted;

            var maxGrowth = Math.Max(8, window.end - window.start);
            var start = window.start;
            var end = window.end;

            if (!coversStart)
            {
                start = Math.Max(
                    0,
                    Math.Max(Math.Min(wanted.start, window.start - 1), window.start - maxGrowth)
                );
            }

            if (!coversEnd)
            {
                // Clamped as a float: the quotient is infinite under an unbounded cache extent, and
                // over a window of zero-extent slots, where only the cap makes the growth geometric.
                var missing = Mathf.Clamp(
                    (coverEnd - windowEndEdge) / Mathf.Max(localStride, 0f),
                    1f,
                    maxGrowth
                );
                end = Math.Min(slotCount, window.end + Mathf.CeilToInt(missing));
            }

            return (start, end);
        }

        /// <summary>
        ///     The slot in [0, <paramref name="slotCount" />) that <paramref name="offset" /> falls in
        ///     when every slot is <paramref name="stride" /> long. Clamped before it becomes an int:
        ///     an unbounded cache extent makes the offset infinite.
        /// </summary>
        public static int SlotAtUniformStride(float offset, float stride, int slotCount) =>
            stride > 0f ? (int)Mathf.Clamp(Mathf.Floor(offset / stride), 0f, slotCount - 1) : 0;

        /// <summary>
        ///     Shifts a child's leading-edge offset to the requested spot in the viewport: Start keeps the
        ///     leading edge; Center/End pull it back by the appropriate slice of the leftover viewport space;
        ///     Nearest keeps <paramref name="currentOffset" /> for a child already fully in view.
        /// </summary>
        public static float AlignToScrollPosition(
            float leadingEdge,
            float childSize,
            float viewportSize,
            float currentOffset,
            ScrollToPosition position
        )
        {
            return position switch
            {
                ScrollToPosition.Center => leadingEdge - (viewportSize - childSize) / 2f,
                ScrollToPosition.End => leadingEdge - (viewportSize - childSize),
                ScrollToPosition.Nearest => AlignToNearestEdge(
                    leadingEdge,
                    childSize,
                    viewportSize,
                    currentOffset
                ),
                _ => leadingEdge,
            };
        }

        private static float AlignToNearestEdge(
            float leadingEdge,
            float childSize,
            float viewportSize,
            float currentOffset
        )
        {
            if (leadingEdge < currentOffset || childSize >= viewportSize)
                return leadingEdge;

            var trailingEdge = leadingEdge + childSize;
            return trailingEdge > currentOffset + viewportSize
                ? trailingEdge - viewportSize
                : currentOffset;
        }
    }
}
