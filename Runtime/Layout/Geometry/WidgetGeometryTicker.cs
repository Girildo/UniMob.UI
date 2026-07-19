using UnityEngine;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// Drives reactive global-geometry tracking. Unity does not signal a widget when it merely moves
    /// (e.g. an ancestor scrolls) without resizing, so a widget's on-screen box can change with no
    /// layout or render event. This ticker exposes a per-frame <see cref="Frame"/> atom that a reactive
    /// <c>GlobalGeometry</c> depends on, forcing a re-measure each frame -- but only for geometries that
    /// are actually being observed, since unobserved computed atoms stay dormant. The single always-on
    /// cost is one integer increment per frame, and it is incurred only after tracking is first used.
    /// </summary>
    internal static class WidgetGeometryTicker
    {
        private static MutableAtom<uint> _frame;
        private static WidgetGeometryTickerDriver _driver;

        public static MutableAtom<uint> Frame
        {
            get
            {
                EnsureStarted();
                return _frame;
            }
        }

        public static void EnsureStarted()
        {
            _frame ??= Atom.Value(0u);

            if (_driver != null)
            {
                return;
            }

            var go = new GameObject(nameof(WidgetGeometryTickerDriver))
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            Object.DontDestroyOnLoad(go);
            _driver = go.AddComponent<WidgetGeometryTickerDriver>();
        }

        internal static void Tick()
        {
            if (_frame == null)
            {
                return;
            }

            using (Atom.NoWatch)
            {
                _frame.Value = unchecked(_frame.Value + 1);
            }
        }
    }

    internal sealed class WidgetGeometryTickerDriver : MonoBehaviour
    {
        private void LateUpdate() => WidgetGeometryTicker.Tick();
    }
}
