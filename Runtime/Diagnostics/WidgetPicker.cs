#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UniMob.UI.Diagnostics
{
    /// <summary>
    ///     Inspect mode: while armed, the next click anywhere in the running UI is captured and
    ///     reported as a screen position instead of reaching the app.
    /// </summary>
    /// <remarks>
    ///     An editor window cannot intercept a click in the Game view -- Unity exposes no hook for it,
    ///     and <c>Handles</c> do not draw there -- so this needs a real object in the running scene.
    ///     Precedented by the in-scene warning overlay, and built the same way: created on demand,
    ///     <see cref="HideFlags.HideAndDontSave"/> so it is never saved into a scene, and destroyed the
    ///     moment it is not needed.
    ///     <para>
    ///         The click is taken by a full-screen graphic on a canvas above everything, which means it
    ///         goes through uGUI's own event system and works whichever input backend the app uses. It
    ///         also means the app does not receive the click, which is deliberate and is what every
    ///         inspector does: you are pointing at a button, not pressing it.
    ///     </para>
    ///     <para>
    ///         Editor-only by compilation. Nothing in a player build should be able to eat input.
    ///     </para>
    /// </remarks>
    public static class WidgetPicker
    {
        private static Overlay _overlay;

        /// <summary>The screen position of a captured click. Fires once; arming is single-shot.</summary>
        public static event Action<Vector2> Picked;

        public static bool IsArmed => _overlay != null;

        public static void Arm()
        {
            if (_overlay != null)
            {
                return;
            }

            var go = new GameObject("UniMob Widget Picker")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above everything the app can plausibly use, so the capture is not itself occluded.
            canvas.sortingOrder = short.MaxValue;

            go.AddComponent<GraphicRaycaster>();

            var target = new GameObject("Surface") { hideFlags = HideFlags.HideAndDontSave };
            target.transform.SetParent(go.transform, worldPositionStays: false);

            var rect = target.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = target.AddComponent<Image>();

            // Fully transparent would still raycast, but a barely-visible tint is the only feedback
            // that inspect mode is on at all once the pointer is over the Game view.
            image.color = new Color(0.25f, 0.5f, 1f, 0.04f);
            image.raycastTarget = true;

            _overlay = target.AddComponent<Overlay>();
        }

        public static void Disarm()
        {
            if (_overlay == null)
            {
                return;
            }

            var root = _overlay.transform.parent != null
                ? _overlay.transform.parent.gameObject
                : _overlay.gameObject;

            _overlay = null;
            UnityEngine.Object.DestroyImmediate(root);
        }

        private sealed class Overlay : MonoBehaviour, IPointerClickHandler
        {
            public void OnPointerClick(PointerEventData eventData)
            {
                var position = eventData.position;

                // Disarm before reporting: the handler selects a widget, which may repaint and
                // re-enter, and a picker that outlives its own click keeps eating input.
                Disarm();
                Picked?.Invoke(position);
            }
        }
    }
}
#endif
