namespace UniMob.UI
{
    /// <summary>
    /// Builds the widget for one slot. The constraint permits a nullable <typeparamref name="TWidget"/>
    /// so that a builder can answer with no widget at all, which empties the slot.
    /// </summary>
    public delegate TWidget WidgetBuilder<out TWidget>(BuildContext context)
        where TWidget : Widget?;
}
