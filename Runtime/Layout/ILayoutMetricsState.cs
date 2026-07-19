using UnityEngine;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// Implemented by the constraint-based layout states, exposing their rendered geometry so a widget
    /// can locate its own -- or, via a <see cref="GlobalKey{T}"/>, another widget's -- box on screen.
    /// This mirrors Flutter's <c>RenderBox</c>: <see cref="LocalSize"/>/<see cref="LocalRect"/> are the
    /// local box (<c>RenderBox.size</c>), and <see cref="TryGetGlobalGeometry"/>/<see cref="GlobalGeometry"/>
    /// are the on-screen box (<c>localToGlobal(Offset.zero) &amp; size</c>).
    /// </summary>
    public interface ILayoutMetricsState : IState
    {
        /// <summary><b>[Atom]</b> The widget's own size in logical pixels (reactive; no polling).</summary>
        Vector2 LocalSize { get; }

        /// <summary><b>[Atom]</b> The widget's local box (origin zero) in logical pixels (reactive; no polling).</summary>
        Rect LocalRect { get; }

        /// <summary>
        /// Reads the widget's current on-screen box in canvas space. Returns <c>false</c> (and
        /// <see cref="WidgetGeometry.Empty"/>) while the widget is not mounted to a view. Cheap and
        /// non-reactive -- suited to one-shot queries (e.g. on a tap).
        /// </summary>
        bool TryGetGlobalGeometry(out WidgetGeometry geometry);

        /// <summary>
        /// <b>[Atom]</b> The widget's on-screen box in canvas space, re-measured every frame while
        /// observed so followers track movement (scroll/animation) that Unity never signals.
        /// <see cref="WidgetGeometry.Empty"/> while the widget is not mounted.
        /// </summary>
        WidgetGeometry GlobalGeometry { get; }
    }
}
