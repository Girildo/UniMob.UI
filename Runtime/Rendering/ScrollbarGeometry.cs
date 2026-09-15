using UnityEngine;

namespace UniMob.UI.Rendering
{
    /// <summary>
    ///     The placement of a scrollbar thumb on its track.
    /// </summary>
    /// <param name="Offset">The distance from the track start to the thumb's leading edge.</param>
    /// <param name="Extent">The thumb's extent along the track.</param>
    internal readonly record struct ThumbGeometry(float Offset, float Extent);

    /// <summary>
    ///     Maps <see cref="ScrollMetrics" /> to a thumb on a track and back: the thumb is proportional to
    ///     the visible fraction of the content until one of the two minimum extents takes over, and then
    ///     travels whatever the resulting extent leaves free, so that the forward mapping and the drag
    ///     mapping agree at every size.
    /// </summary>
    internal static class ScrollbarGeometry
    {
        /// <summary>
        ///     Places the thumb for <paramref name="metrics" /> on a track of
        ///     <paramref name="trackExtent" />. Returns null when there is nothing to scroll, when the
        ///     content is empty, or when the track has no extent: such a scrollbar shows no thumb at all.
        ///     The thumb is at least <paramref name="minThumbExtent" /> long, shrinks by the overscrolled
        ///     amount down to <paramref name="minOverscrollThumbExtent" /> while pinned to the end being
        ///     overscrolled, and never exceeds the track.
        /// </summary>
        public static ThumbGeometry? Thumb(
            ScrollMetrics metrics,
            float trackExtent,
            float minThumbExtent,
            float minOverscrollThumbExtent
        )
        {
            if (!metrics.CanScroll || trackExtent <= 0f || metrics.ContentExtent <= 0f)
                return null;

            var maxScrollExtent = metrics.MaxScrollExtent;

            var nominalExtent = Mathf.Clamp(
                trackExtent * metrics.ViewportExtent / metrics.ContentExtent,
                Mathf.Min(minThumbExtent, trackExtent),
                trackExtent
            );

            var overscroll =
                Mathf.Max(0f, -metrics.PixelOffset)
                + Mathf.Max(0f, metrics.PixelOffset - maxScrollExtent);

            var extent =
                overscroll > 0f
                    ? Mathf.Max(
                        nominalExtent - overscroll,
                        Mathf.Min(minOverscrollThumbExtent, trackExtent)
                    )
                    : nominalExtent;

            // The travel uses the final extent, not the nominal one, so that a thumb held at a minimum
            // still maps back through PixelDeltaForThumbDelta to the offset it was drawn from.
            var clampedOffset = Mathf.Clamp(metrics.PixelOffset, 0f, maxScrollExtent);
            var offset = clampedOffset / maxScrollExtent * (trackExtent - extent);

            return new ThumbGeometry(offset, extent);
        }

        /// <summary>
        ///     Converts a thumb movement of <paramref name="thumbDelta" /> pixels into the scroll offset
        ///     movement it stands for, for a thumb of <paramref name="thumbExtent" /> on a track of
        ///     <paramref name="trackExtent" />. Returns 0 when the thumb fills the track or there is
        ///     nothing to scroll. Each pointer delta is converted against the metrics current at that
        ///     update, so an estimated content extent that changes between updates changes the ratio
        ///     without ever reversing the direction of a drag.
        /// </summary>
        public static float PixelDeltaForThumbDelta(
            ScrollMetrics metrics,
            float trackExtent,
            float thumbExtent,
            float thumbDelta
        )
        {
            var travel = trackExtent - thumbExtent;
            if (travel <= 0f || !metrics.CanScroll)
                return 0f;

            return thumbDelta * metrics.MaxScrollExtent / travel;
        }

        /// <summary>
        ///     The offset one viewport before (<paramref name="towardStart" />) or after the current one,
        ///     clamped to the scrollable range. This is where a tap on the track scrolls to.
        /// </summary>
        public static float PageTarget(ScrollMetrics metrics, bool towardStart)
        {
            var target = towardStart
                ? metrics.PixelOffset - metrics.ViewportExtent
                : metrics.PixelOffset + metrics.ViewportExtent;

            return Mathf.Clamp(target, 0f, metrics.MaxScrollExtent);
        }
    }
}
