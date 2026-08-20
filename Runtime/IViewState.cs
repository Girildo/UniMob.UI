using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;

namespace UniMob.UI
{
    public interface IViewState : IState
    {
        WidgetViewReference View { get; }

        void DidViewMount(IView view);
        void DidViewUnmount(IView view);

        /// <summary>
        /// The view this state is currently painted by, or <c>null</c> while it is not mounted to one.
        /// </summary>
        /// <remarks>
        /// Exists so a diagnostic can attach the offending GameObject to a console entry, which is what
        /// makes the entry select the widget when clicked. Plain and non-reactive: it is written by
        /// <see cref="DidViewMount"/> and read from inside a layout pass, so it must not be an atom.
        /// </remarks>
        IView? MountedView { get; }

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
