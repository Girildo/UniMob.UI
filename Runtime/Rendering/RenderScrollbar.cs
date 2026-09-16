using UniMob.UI.Diagnostics;
using UnityEngine;

namespace UniMob.UI.Rendering
{
    /// <summary>
    ///     The placement of a scrollbar thumb on its track.
    /// </summary>
    /// <param name="Offset">The distance from the track start to the thumb's leading edge.</param>
    /// <param name="Extent">The thumb's extent along the track.</param>
    internal readonly record struct ThumbPlacement(float Offset, float Extent);

    /// <summary>
    ///     Lays out a scrollbar: the track fills its box along the state's axis and is
    ///     <see cref="IScrollbarState.Thickness" /> across, and the thumb is placed on it from the
    ///     attached scrollable's metrics.
    /// </summary>
    /// <remarks>
    ///     The track's own size never depends on the scroll offset, so a parent measuring this through
    ///     <see cref="RenderObject.WatchedSize" /> is not dragged into a relayout on every scroll;
    ///     only the thumb's position moves.
    ///     <para>
    ///         The mapping between metrics and thumb runs both ways: the thumb is proportional to the
    ///         visible fraction of the content until one of the two minimum extents takes over, and
    ///         then travels whatever the resulting extent leaves free, so that the forward mapping and
    ///         the drag mapping agree at every size.
    ///     </para>
    ///     <para>
    ///         The instance methods answer about the geometry the last pass drew, and read nothing
    ///         reactively: they serve pointer handlers, which must add no dependency to whatever
    ///         computation they run inside.
    ///     </para>
    /// </remarks>
    public class RenderScrollbar : SingleChildRenderObject
    {
        private const string BoundTheTrack =
            "A scrollbar fills its box along its axis, so it needs a bounded extent there: place it "
            + "in a Positioned with both ends set, an Expanded, or a SizedBox.";

        private readonly IScrollbarState _state;

        public RenderScrollbar(IScrollbarState state)
            : base(state)
        {
            _state = state;
        }

        /// <summary>
        ///     Where the thumb was placed on the last pass, or <c>null</c> when the pass drew none.
        ///     Read by the drag and tap handlers, which map a pointer position against the geometry
        ///     the user can actually see.
        /// </summary>
        internal ThumbPlacement? Thumb { get; private set; }

        /// <summary>
        ///     The scroll offset movement that moving the thumb by <paramref name="thumbDelta" />
        ///     pixels stands for, against the thumb and track of the last pass. Returns 0 when that
        ///     pass drew no thumb or the scrollable reports no metrics.
        /// </summary>
        public float ScrollDeltaFor(float thumbDelta)
        {
            if (Thumb is not { } thumb || CurrentMetrics() is not { } metrics)
                return 0f;

            var size = PeekSize();
            var trackExtent = _state.Axis == Axis.Horizontal ? size.x : size.y;

            return ScrollDeltaForThumbDelta(metrics, trackExtent, thumb.Extent, thumbDelta);
        }

        /// <summary>
        ///     The offset a tap at <paramref name="trackPosition" /> pages to, measured along the axis
        ///     from the track start of the last pass. Returns <c>null</c> when the tap lands on the
        ///     thumb, when that pass drew no thumb, or when the scrollable reports no metrics.
        /// </summary>
        public float? PageTargetFor(float trackPosition)
        {
            if (Thumb is not { } thumb || CurrentMetrics() is not { } metrics)
                return null;

            if (trackPosition >= thumb.Offset && trackPosition <= thumb.Offset + thumb.Extent)
                return null;

            return PageTarget(metrics, towardStart: trackPosition < thumb.Offset);
        }

        /// <summary>
        ///     Places the thumb for <paramref name="metrics" /> on a track of
        ///     <paramref name="trackExtent" />. Returns null when there is nothing to scroll, when the
        ///     content is empty, or when the track has no extent: such a scrollbar shows no thumb at all.
        ///     The thumb is at least <paramref name="minThumbExtent" /> long, shrinks by the overscrolled
        ///     amount down to <paramref name="minOverscrollThumbExtent" /> while pinned to the end being
        ///     overscrolled, and never exceeds the track.
        /// </summary>
        internal static ThumbPlacement? PlaceThumb(
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
            // still maps back through ScrollDeltaForThumbDelta to the offset it was drawn from.
            var clampedOffset = Mathf.Clamp(metrics.PixelOffset, 0f, maxScrollExtent);
            var offset = clampedOffset / maxScrollExtent * (trackExtent - extent);

            return new ThumbPlacement(offset, extent);
        }

        /// <summary>
        ///     Converts a thumb movement of <paramref name="thumbDelta" /> pixels into the scroll offset
        ///     movement it stands for, for a thumb of <paramref name="thumbExtent" /> on a track of
        ///     <paramref name="trackExtent" />. Returns 0 when the thumb fills the track or there is
        ///     nothing to scroll. Each pointer delta is converted against the metrics current at that
        ///     update, so an estimated content extent that changes between updates changes the ratio
        ///     without ever reversing the direction of a drag.
        /// </summary>
        internal static float ScrollDeltaForThumbDelta(
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
        internal static float PageTarget(ScrollMetrics metrics, bool towardStart)
        {
            var target = towardStart
                ? metrics.PixelOffset - metrics.ViewportExtent
                : metrics.PixelOffset + metrics.ViewportExtent;

            return Mathf.Clamp(target, 0f, metrics.MaxScrollExtent);
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            var horizontal = _state.Axis == Axis.Horizontal;
            var bounded = horizontal ? constraints.HasBoundedWidth : constraints.HasBoundedHeight;

            if (!bounded)
            {
                ReportUnboundedConstraint(
                    horizontal ? LayoutAxes.Horizontal : LayoutAxes.Vertical,
                    constraints,
                    BoundTheTrack
                );
            }

            var trackExtent =
                !bounded ? 0f
                : horizontal ? constraints.MaxWidth
                : constraints.MaxHeight;

            var thickness = horizontal
                ? constraints.ConstrainHeight(_state.Thickness)
                : constraints.ConstrainWidth(_state.Thickness);

            var metrics = _state.Metrics;

            Thumb = metrics.HasValue
                ? PlaceThumb(
                    metrics.Value,
                    trackExtent,
                    _state.MinThumbExtent,
                    _state.MinOverscrollThumbExtent
                )
                : null;

            // Laid out on every pass, thumb or no thumb: a child a sizing pass skips is not laid out
            // small, it is not laid out at all, and its view then reports being rendered before
            // anything laid it out.
            var childConstraints = Thumb is { } thumb
                ? horizontal
                    ? LayoutConstraints.Tight(thumb.Extent, thickness)
                    : LayoutConstraints.Tight(thickness, thumb.Extent)
                : LayoutConstraints.Tight(0f, 0f);

            var child = Child;
            ChildSize = child != null ? LayoutChild(child, childConstraints) : Vector2.zero;

            var size = horizontal
                ? new Vector2(trackExtent, thickness)
                : new Vector2(thickness, trackExtent);

            return constraints.Constrain(size);
        }

        protected override void PerformPositioning(Vector2 size)
        {
            var offset = Thumb?.Offset ?? 0f;

            ChildPosition =
                _state.Axis == Axis.Horizontal ? new Vector2(offset, 0f) : new Vector2(0f, offset);
        }

        protected override float ComputeIntrinsicWidth(float height) =>
            _state.Axis == Axis.Horizontal ? 0f : _state.Thickness;

        protected override float ComputeIntrinsicHeight(float width) =>
            _state.Axis == Axis.Horizontal ? _state.Thickness : 0f;

        private ScrollMetrics? CurrentMetrics()
        {
            using (Atom.NoWatch)
            {
                return _state.Metrics;
            }
        }
    }
}
