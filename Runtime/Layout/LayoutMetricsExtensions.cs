namespace UniMob.UI.Layout
{
    /// <summary>
    /// Lets a widget locate <b>itself</b> (or its nearest enclosing layout widget) without a key. To
    /// locate <b>another</b> widget across the tree, attach a <see cref="WidgetGeometryKey"/> to it and
    /// query that.
    /// </summary>
    public static class LayoutMetricsExtensions
    {
        /// <summary>
        /// The nearest layout metrics state at or above <paramref name="context"/> -- the analog of
        /// Flutter's <c>context.findRenderObject()</c>. Returns <c>null</c> when no enclosing layout
        /// widget exists.
        /// </summary>
        public static ILayoutMetricsState FindLayoutMetrics(this BuildContext context)
        {
            return context?.AncestorStateOfType<ILayoutMetricsState>();
        }
    }
}
