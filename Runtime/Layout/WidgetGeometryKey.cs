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
        private ILayoutMetricsState State => UntypedCurrentState as ILayoutMetricsState;

        public override bool Equals(Key other) => ReferenceEquals(this, other);

        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

        public override string ToString() => $"[WidgetGeometryKey #{RuntimeHelpers.GetHashCode(this)}]";

        /// <summary>
        /// One-shot read of the keyed widget's on-screen box in canvas space. Returns <c>false</c> (and
        /// <see cref="WidgetGeometry.Empty"/>) while the widget is not mounted. Cheap and non-reactive.
        /// </summary>
        public bool TryGetGlobalGeometry(out WidgetGeometry geometry)
        {
            var state = State;
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
        public WidgetGeometry GlobalGeometry => State?.GlobalGeometry ?? WidgetGeometry.Empty;

        /// <summary><b>[Atom]</b> The keyed widget's own size in logical pixels, or <c>null</c> while unmounted.</summary>
        public Vector2? LocalSize => State?.LocalSize;
    }
}
