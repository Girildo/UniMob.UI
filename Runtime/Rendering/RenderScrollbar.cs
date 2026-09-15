using UniMob.UI.Diagnostics;
using UnityEngine;

namespace UniMob.UI.Rendering
{
    /// <summary>
    ///     Lays out a scrollbar: the track fills its box along the state's axis and is
    ///     <see cref="IScrollbarState.Thickness" /> across, and the thumb is placed on it from the
    ///     attached scrollable's metrics.
    /// </summary>
    /// <remarks>
    ///     The track's own size never depends on the scroll offset, so a parent measuring this through
    ///     <see cref="RenderObject.WatchedSize" /> is not dragged into a relayout on every scroll;
    ///     only the thumb's position moves.
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
        internal ThumbGeometry? Thumb { get; private set; }

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
                ? ScrollbarGeometry.Thumb(
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
    }
}
