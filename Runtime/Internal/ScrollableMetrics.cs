using UniMob.UI.Rendering;

namespace UniMob.UI.Internal
{
    /// <summary>
    ///     Measures a scrolling list's geometry along its axis, for the states that publish it as
    ///     <c>IScrollControllerExecutor.Metrics</c>. One implementation, so that every virtualized
    ///     scrollable reports the same numbers and depends on the same atoms.
    /// </summary>
    internal static class ScrollableMetrics
    {
        /// <summary>
        ///     The geometry of <paramref name="state" /> along its scroll axis, or <c>null</c> before
        ///     anything has laid it out. Reactive: the caller wraps it in an <c>[Atom]</c>, so that the
        ///     memo belongs to the state rather than to this method.
        /// </summary>
        public static ScrollMetrics? Measure(IScrollingListState state)
        {
            var renderObject = state.RenderObject;

            // Constraints first, and never WatchLayout on a render object nothing has laid out:
            // the constraints atom is what a reader has to depend on to wake on the first pass,
            // and WatchLayout reports a never-laid-out read as an error in the Editor.
            if (renderObject == null || !renderObject.Constraints.HasValue)
                return null;

            // WatchLayout, not WatchedSize: TotalContentSize() is a plain method whose lazy
            // estimate refines on passes that leave the viewport exactly as big as it was.
            var viewport = renderObject.WatchLayout();
            var axis = state.Axis;

            return new ScrollMetrics(
                state.ScrollController.PixelOffset,
                ((IScrollableRenderObject)renderObject).TotalContentSize(),
                axis == Axis.Horizontal ? viewport.x : viewport.y,
                axis
            );
        }
    }
}
