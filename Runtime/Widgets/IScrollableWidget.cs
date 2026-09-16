namespace UniMob.UI.Widgets
{
    /// <summary>
    ///     A virtualized scrollable: the widget a <see cref="UniMob.UI.ScrollController" /> attaches to.
    /// </summary>
    public interface IScrollableWidget : Widget
    {
        /// <summary>
        ///     The controller the caller gave, or <c>null</c> when the scrollable makes its own, which
        ///     cannot be reached from the widget.
        /// </summary>
        ScrollController? ScrollController { get; }

        /// <summary>The axis the scrollable scrolls along, and the one its metrics are read along.</summary>
        Axis Axis { get; }
    }
}
