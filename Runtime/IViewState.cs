using UniMob.UI.Layout;

namespace UniMob.UI
{
    public interface IViewState : IState
    {
        WidgetViewReference View { get; }

        void DidViewMount(IView view);
        void DidViewUnmount(IView view);

        /// <summary>
        /// Reads the widget's current on-screen box in canvas space. Returns <c>false</c> (and
        /// <see cref="WidgetGeometry.Empty"/>) while the widget is not mounted to a view. Cheap and
        /// non-reactive -- suited to one-shot queries (e.g. on a tap).
        /// </summary>
        /// <remarks>
        /// On-screen geometry lives here, and not on the layout tree, because it is a Unity-boundary
        /// query: it needs the mounted view and the root canvas, and it re-measures rather than
        /// re-deriving. Render objects deliberately do not redo Unity's transform chain, which is
        /// exactly why this cannot be answered from one.
        /// </remarks>
        bool TryGetGlobalGeometry(out WidgetGeometry geometry);

        /// <summary>
        /// <b>[Atom]</b> The widget's on-screen box in canvas space, re-measured every frame while
        /// observed so followers track movement (scroll/animation) that Unity never signals.
        /// <see cref="WidgetGeometry.Empty"/> while the widget is not mounted.
        /// </summary>
        WidgetGeometry GlobalGeometry { get; }
    }
}
