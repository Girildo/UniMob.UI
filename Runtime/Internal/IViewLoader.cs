namespace UniMob.UI.Internal
{
    public interface IViewLoader
    {
        /// <summary>
        /// The view template for <paramref name="viewReference"/>, or <c>null</c> when this loader
        /// does not handle references of that kind or cannot resolve this one.
        /// </summary>
        IView? LoadViewPrefab(WidgetViewReference viewReference);
    }
}
