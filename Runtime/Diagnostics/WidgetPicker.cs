#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UniMob.UI.Diagnostics
{
    /// <summary>
    ///     Inspect mode: while armed, the running UI is dimmed, the widget under the pointer is lit,
    ///     and the next click is captured and reported instead of reaching the app.
    /// </summary>
    /// <remarks>
    ///     An editor window cannot intercept a click in the Game view -- Unity exposes no hook for it,
    ///     and <c>Handles</c> do not draw there -- so this needs a real object in the running scene.
    ///     Precedented by the in-scene warning overlay, and built the same way: created on demand,
    ///     <see cref="HideFlags.HideAndDontSave"/> so it is never saved into a scene, and destroyed the
    ///     moment it is not needed.
    ///     <para>
    ///         Pointer events arrive through uGUI's own event system, so this works whichever input
    ///         backend the app uses. The app does not receive them, which is deliberate and is what
    ///         every inspector does: you are pointing at a button, not pressing it.
    ///     </para>
    ///     <para>
    ///         Editor-only by compilation. Nothing in a player build should be able to eat input.
    ///     </para>
    /// </remarks>
    public static class WidgetPicker
    {
        private const int Bands = 4;
        private const float BorderThickness = 2f;

        private static readonly Color DimColour = new Color(0.05f, 0.07f, 0.12f, 0.55f);
        private static readonly Color BorderColour = new Color(0.35f, 0.72f, 1f, 0.95f);

        private static Overlay _overlay;
        private static RectTransform[] _dim;
        private static RectTransform[] _border;

        /// <summary>A captured click, in screen coordinates. Arming is single-shot.</summary>
        public static event Action<Vector2> Picked;

        /// <summary>The pointer moved, in screen coordinates.</summary>
        public static event Action<Vector2> Hovered;

        public static bool IsArmed => _overlay != null;

        public static void Arm()
        {
            if (_overlay != null)
            {
                return;
            }

            var root = new GameObject("UniMob Widget Picker")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above anything the app can plausibly use, so neither the dim nor the capture is occluded.
            canvas.sortingOrder = short.MaxValue;

            root.AddComponent<GraphicRaycaster>();

            // The dim goes down first so the border and the click surface sit above it.
            _dim = new RectTransform[Bands];
            for (var i = 0; i < Bands; i++)
            {
                _dim[i] = CreatePiece(root.transform, "Dim", DimColour);
            }

            _border = new RectTransform[Bands];
            for (var i = 0; i < Bands; i++)
            {
                _border[i] = CreatePiece(root.transform, "Border", BorderColour);
            }

            // Transparent: an Image raycasts regardless of alpha, and the dim already says that
            // inspect mode is on. Last, so it is the topmost thing under the pointer.
            var surface = CreatePiece(root.transform, "Surface", Color.clear);
            surface.anchorMin = Vector2.zero;
            surface.anchorMax = Vector2.one;
            surface.offsetMin = Vector2.zero;
            surface.offsetMax = Vector2.zero;
            surface.GetComponent<Image>().raycastTarget = true;

            _overlay = surface.gameObject.AddComponent<Overlay>();

            // Nothing hovered yet, so the dim covers everything: one uniform sheet, which is also the
            // clearest possible signal that the mode is on.
            ClearHighlight();
        }

        public static void Disarm()
        {
            if (_overlay == null)
            {
                return;
            }

            var root = _overlay.transform.parent.gameObject;

            _overlay = null;
            _dim = null;
            _border = null;

            UnityEngine.Object.DestroyImmediate(root);
        }

        /// <summary>
        ///     Cuts <paramref name="screenRect"/> out of the dim and outlines it.
        /// </summary>
        /// <remarks>
        ///     A dim with a hole in it, rather than a tint laid over the widget: the hole leaves it
        ///     showing in its own colours, which is the whole point of looking at it. With a single hole
        ///     the general band decomposition degenerates to four rectangles, so it is written out
        ///     rather than solved.
        /// </remarks>
        public static void SetHighlight(Rect screenRect)
        {
            if (_overlay == null)
            {
                return;
            }

            var w = Screen.width;
            var h = Screen.height;

            var left = Mathf.Clamp(screenRect.xMin, 0, w);
            var right = Mathf.Clamp(screenRect.xMax, 0, w);
            var bottom = Mathf.Clamp(screenRect.yMin, 0, h);
            var top = Mathf.Clamp(screenRect.yMax, 0, h);

            Place(_dim[0], Rect.MinMaxRect(0, top, w, h));
            Place(_dim[1], Rect.MinMaxRect(0, 0, w, bottom));
            Place(_dim[2], Rect.MinMaxRect(0, bottom, left, top));
            Place(_dim[3], Rect.MinMaxRect(right, bottom, w, top));

            var t = BorderThickness;
            Place(_border[0], Rect.MinMaxRect(left - t, top, right + t, top + t));
            Place(_border[1], Rect.MinMaxRect(left - t, bottom - t, right + t, bottom));
            Place(_border[2], Rect.MinMaxRect(left - t, bottom, left, top));
            Place(_border[3], Rect.MinMaxRect(right, bottom, right + t, top));
        }

        /// <summary>Dims everything, with nothing lit.</summary>
        public static void ClearHighlight()
        {
            if (_overlay == null)
            {
                return;
            }

            Place(_dim[0], new Rect(0, 0, Screen.width, Screen.height));

            for (var i = 1; i < Bands; i++)
            {
                Place(_dim[i], Rect.zero);
                Place(_border[i], Rect.zero);
            }

            Place(_border[0], Rect.zero);
        }

        private static RectTransform CreatePiece(Transform parent, string name, Color colour)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(parent, worldPositionStays: false);

            var rect = go.AddComponent<RectTransform>();

            // Anchored to the bottom-left corner with a bottom-left pivot, so a piece can be placed in
            // screen pixels directly. There is no CanvasScaler here, so the canvas is the screen.
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;

            return rect;
        }

        private static void Place(RectTransform rect, Rect screenRect)
        {
            rect.anchoredPosition = new Vector2(screenRect.xMin, screenRect.yMin);
            rect.sizeDelta = new Vector2(
                Mathf.Max(0f, screenRect.width),
                Mathf.Max(0f, screenRect.height)
            );
        }

        private sealed class Overlay : MonoBehaviour, IPointerClickHandler, IPointerMoveHandler
        {
            public void OnPointerMove(PointerEventData eventData) =>
                Hovered?.Invoke(eventData.position);

            public void OnPointerClick(PointerEventData eventData)
            {
                var position = eventData.position;

                // Disarm before reporting: the handler selects a widget, which repaints and may
                // re-enter, and a picker that outlives its own click keeps eating input.
                Disarm();
                Picked?.Invoke(position);
            }
        }
    }
}
#endif
