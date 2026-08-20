namespace UniMob.UI.Rendering
{
    /// <summary>
    ///     Everything <see cref="Views.ScrollListView" /> needs from the render object driving it, so the
    ///     same view (and prefab) can serve any single-axis scrollable sliver -- both
    ///     <see cref="RenderSliverList" /> and <see cref="RenderSliverGrid" /> implement it. Extends
    ///     <see cref="IMultiChildrenRenderObject" /> for the per-child <c>ChildrenLayout</c> the view
    ///     positions from.
    /// </summary>
    public interface IScrollableRenderObject : IMultiChildrenRenderObject
    {
        /// <summary>
        ///     Total main-axis size of the scrollable content; the view sizes its content rect to this, so
        ///     positioned content must end exactly here (max scroll == this minus the viewport).
        /// </summary>
        float TotalContentSize();

        /// <summary>
        ///     Target scroll offset in pixels to bring <paramref name="index" /> into view at
        ///     <paramref name="position" />.
        /// </summary>
        float CalculateScrollPixelOffset(int index, ScrollToPosition position);
    }
}
