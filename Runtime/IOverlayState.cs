namespace UniMob.UI
{
    /// <summary>
    ///     A state that presents a set of independently-lived entries to the layout system. Insertion
    ///     order is paint order, and every entry fills the layer.
    /// </summary>
    /// <remarks>
    ///     Adds nothing to <see cref="IMultiChildLayoutState"/> beyond a name: what it buys is a seam,
    ///     so <see cref="Rendering.RenderOverlay"/> states what it lays out and can be driven by
    ///     something other than a mounted <c>OverlayState</c>.
    /// </remarks>
    public interface IOverlayState : IMultiChildLayoutState { }
}
