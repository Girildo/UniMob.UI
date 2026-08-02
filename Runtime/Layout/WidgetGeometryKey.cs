using System.Runtime.CompilerServices;
using UnityEngine;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// The identity handle behind "where is this widget on screen?". Attach one to a constraint-based
    /// layout widget's <c>Key</c> (create it once, e.g. as a field -- never inline in <c>Build</c>), then
    /// read <see cref="GlobalGeometry"/> (reactive) or call <see cref="TryGetGlobalGeometry"/> (one-shot)
    /// from anywhere to locate that widget's rendered box.
    /// <para>
    /// This is a purpose-built <see cref="GlobalKey"/>: consumers never touch the underlying state
    /// interface. It reports <see cref="WidgetGeometry.Empty"/> while the keyed widget is unmounted (or if
    /// it is attached to a widget that has no rendered box). Uses reference identity, so -- like Flutter's
    /// <c>GlobalKey</c> -- one instance must be attached to at most one live widget at a time.
    /// </para>
    /// </summary>
    public sealed class WidgetGeometryKey : GlobalKey
    {
        // Resolved through the tree links rather than by casting the keyed state to a geometry
        // interface. A build-only wrapper (a HocState, a StatelessWidget) owns no view of its own, so
        // the cast failed and the key silently reported Empty -- even though InflateWidget binds a
        // GlobalKey unconditionally, so attaching one there looked like it had worked. A wrapper does
        // render exactly one subtree, and InnerViewState already recurses to the view at the top of it,
        // so the key now measures the box the widget actually renders whatever state type backs it.
        // For ViewState-backed widgets this resolves to the same object as before.
        private IViewState View => UntypedCurrentState?.InnerViewState;

        public override bool Equals(Key other) => ReferenceEquals(this, other);

        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

        public override string ToString() => $"[WidgetGeometryKey #{RuntimeHelpers.GetHashCode(this)}]";

        /// <summary>
        /// One-shot read of the keyed widget's on-screen box in canvas space. Returns <c>false</c> (and
        /// <see cref="WidgetGeometry.Empty"/>) while the widget is not mounted. Cheap and non-reactive.
        /// </summary>
        public bool TryGetGlobalGeometry(out WidgetGeometry geometry)
        {
            var state = View;
            if (state == null)
            {
                geometry = WidgetGeometry.Empty;
                return false;
            }

            return state.TryGetGlobalGeometry(out geometry);
        }

        /// <summary>
        /// <b>[Atom]</b> The keyed widget's on-screen box in canvas space, tracking movement while
        /// observed; <see cref="WidgetGeometry.Empty"/> while the widget is unmounted or the key is unbound.
        /// </summary>
        public WidgetGeometry GlobalGeometry => View?.GlobalGeometry ?? WidgetGeometry.Empty;

        /// <summary><b>[Atom]</b> The keyed widget's own size in logical pixels, or <c>null</c> while unmounted.</summary>
        /// <remarks>
        /// Observes with the equality cutoff, so a reaction on this fires when the box actually changes
        /// size and not merely when its contents were laid out again. That is what lets "tell me when
        /// my box changes" be answered from outside layout, on the scheduler, instead of by handing a
        /// callback to a render object to invoke mid-sizing.
        /// </remarks>
        public Vector2? LocalSize => UntypedCurrentState?.RenderObject?.WatchedSize();
    }
}
