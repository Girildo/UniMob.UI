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

        /// <summary>How much of the highlight colour is laid over the widget itself.</summary>
        /// <remarks>
        ///     Light on purpose. A layout inspector is often pointed at exactly the pixels in question,
        ///     so the widget has to keep its own colours; Chrome can afford a saturated overlay because
        ///     distinguishing content from padding from margin is the whole point of its one, and this
        ///     draws a single box.
        /// </remarks>
        private const float FillAlpha = 0.10f;

        /// <summary>
        ///     A wash over the whole app while picking, which is the only persistent sign that clicks
        ///     are being swallowed.
        /// </summary>
        /// <remarks>
        ///     Faint, because it is a mode indicator rather than a way of making the target stand out --
        ///     the outline does that. It used to be a heavy dim with the target cut out of it, which put
        ///     all the feedback in the surroundings and therefore had a degenerate case: a full-screen
        ///     widget leaves nothing to dim, so the overlay rendered as nothing whatsoever and read as a
        ///     broken tool. Marking the target instead has no such case.
        /// </remarks>
        private static readonly Color ScrimColour = new Color(0.05f, 0.07f, 0.12f, 0.12f);

        private static readonly Color BorderColour = new Color(0.35f, 0.72f, 1f, 0.95f);

        // Hit testing by box is a different mode with different answers, and the outline is the one
        // thing already in the eye's path -- a toolbar toggle is not, while you are looking at the app.
        // Violet rather than the amber a fault badge uses, which would read as something being wrong.
        private static readonly Color GeometricBorderColour = new Color(0.80f, 0.55f, 1f, 0.95f);

        /// <summary>
        ///     The overlay's pieces, which are built together by <see cref="Arm"/> and destroyed
        ///     together by <see cref="Disarm"/>. One object rather than seven fields so that having
        ///     checked for the overlay is having checked for every piece of it.
        /// </summary>
        private sealed class Overlaid
        {
            public Overlaid(
                Overlay overlay,
                RectTransform canvasRect,
                RectTransform scrim,
                RectTransform fill,
                Image fillImage,
                RectTransform[] border,
                Image[] borderImage
            )
            {
                Overlay = overlay;
                CanvasRect = canvasRect;
                Scrim = scrim;
                Fill = fill;
                FillImage = fillImage;
                Border = border;
                BorderImage = borderImage;
            }

            public readonly Overlay Overlay;
            public readonly RectTransform CanvasRect;
            public readonly RectTransform Scrim;
            public readonly RectTransform Fill;
            public readonly Image FillImage;
            public readonly RectTransform[] Border;
            public readonly Image[] BorderImage;
        }

        private static Overlaid? _overlaid;

        /// <summary>
        ///     Whether the overlay is still hunting, as opposed to merely outlining what was found.
        /// </summary>
        private static bool _picking;

        /// <summary>A captured click, in screen coordinates. Fires for as long as the mode is on.</summary>
        public static event Action<Vector2>? Picked;

        /// <summary>The pointer moved, in screen coordinates.</summary>
        public static event Action<Vector2>? Hovered;

        /// <summary>The pointer left the app, so whatever it was lighting is no longer the subject.</summary>
        public static event Action? Exited;

        public static bool IsArmed => _overlaid != null;

        /// <summary>
        ///     Whether a click would still pick something, as opposed to reaching the app.
        /// </summary>
        /// <remarks>
        ///     Two states, not one, because a picker and a highlight have different lifetimes: hunting
        ///     ends at the pick, and the outline on what you found has to outlive it -- otherwise
        ///     selecting a row in the window stops lighting anything the moment you use the picker once.
        ///     A browser's element picker behaves exactly this way.
        /// </remarks>
        public static bool IsPicking => _overlaid != null && _picking;

        /// <summary>
        ///     Whether the pointer is over the app, and therefore whether hover owns the highlight.
        /// </summary>
        /// <remarks>
        ///     Two things want to light a widget -- the pointer and the tree selection -- and they must
        ///     not fight over it. The pointer wins while it is in the app, because that is the more
        ///     immediate intent; the selection owns it the rest of the time.
        /// </remarks>
        public static bool IsPointerOverApp { get; private set; }

        /// <summary>
        ///     Hands the highlight back to whoever else wants it, without waiting for a pointer-exit.
        /// </summary>
        /// <remarks>
        ///     <see cref="IsPointerOverApp"/> is raised by a pointer move and lowered by a pointer exit,
        ///     and moving from the Game view straight to an editor window delivers neither -- uGUI has no
        ///     reason to send an exit for a pointer that simply stopped being over anything it draws. The
        ///     flag then stays raised, hover keeps ownership of the highlight forever, and clicking rows
        ///     in the window appears to do nothing. A window taking focus is the missing signal.
        /// </remarks>
        public static void ReleasePointer() => IsPointerOverApp = false;

        /// <summary>
        ///     Whether Alt is down, as the accelerator for hit testing by box instead of by raycast.
        /// </summary>
        /// <remarks>
        ///     Read here rather than in the window, because the question is asked while handling a
        ///     pointer event raised by the scene -- there is no <c>Event.current</c> to consult at that
        ///     point. Legacy input only, and false when it is compiled out: this is a shortcut for the
        ///     toolbar toggle that does the same thing, so losing it costs discoverability nothing and
        ///     is a better trade than making a UI package depend on an input backend.
        /// </remarks>
        public static bool IsAltHeld
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
#else
                return false;
#endif
            }
        }

        public static void Arm()
        {
            if (_overlaid != null)
            {
                // Already there, and possibly only outlining a previous pick -- so this is what
                // re-arming after a pick runs through, not a no-op.
                StartPicking();
                return;
            }

            var root = new GameObject("UniMob Widget Picker")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above anything the app can plausibly use, so neither the dim nor the capture is
            // occluded. Layer first: a sorting layer beats a sorting order outright, so being at the
            // top of the wrong layer would lose to anything on a higher one.
            var layers = SortingLayer.layers;
            canvas.sortingLayerID = layers[layers.Length - 1].id;
            canvas.sortingOrder = short.MaxValue;

            root.AddComponent<GraphicRaycaster>();

            var canvasRect = (RectTransform)root.transform;

            // Painted in this order, because a later sibling draws over an earlier one: the wash over
            // the whole app, then the mark on the widget, then its outline, then the click surface.
            var scrim = CreatePiece(root.transform, "Scrim", ScrimColour);

            var fill = CreatePiece(root.transform, "Fill", Fill(BorderColour));
            var fillImage = fill.GetComponent<Image>();

            var border = new RectTransform[Bands];
            var borderImage = new Image[Bands];
            for (var i = 0; i < Bands; i++)
            {
                border[i] = CreatePiece(root.transform, "Border", BorderColour);
                borderImage[i] = border[i].GetComponent<Image>();
            }

            // Transparent: an Image raycasts regardless of alpha, and the dim already says that
            // inspect mode is on. Last, so it is the topmost thing under the pointer.
            var surface = CreatePiece(root.transform, "Surface", Color.clear);
            surface.anchorMin = Vector2.zero;
            surface.anchorMax = Vector2.one;
            surface.offsetMin = Vector2.zero;
            surface.offsetMax = Vector2.zero;
            surface.GetComponent<Image>().raycastTarget = true;

            _overlaid = new Overlaid(
                surface.gameObject.AddComponent<Overlay>(),
                canvasRect,
                scrim,
                fill,
                fillImage,
                border,
                borderImage
            );

            StartPicking();
        }

        /// <summary>Takes the pointer and dims the app: a click from here is a pick.</summary>
        private static void StartPicking()
        {
            _picking = true;
            SetSurfaceCatchesClicks(true);

            // Nothing hovered yet, so the dim covers everything: one uniform sheet, which is also the
            // clearest possible signal that the mode is on.
            ClearHighlight();
        }

        /// <summary>
        ///     Ends the hunt, keeping the outline. The app gets its input back and the dim goes away.
        /// </summary>
        /// <remarks>
        ///     What a pick does. The overlay stays because it is also how the window lights whatever you
        ///     select in it, and tearing it down would make that work only until you first used the
        ///     picker. Re-arming goes through <see cref="Arm"/>, which finds the overlay and resumes.
        /// </remarks>
        public static void StopPicking()
        {
            var overlaid = _overlaid;
            if (overlaid == null)
            {
                return;
            }

            _picking = false;
            SetSurfaceCatchesClicks(false);
            IsPointerOverApp = false;

            // The wash belongs to the hunt, and would otherwise sit over an app the user has their
            // input back on. What was found keeps its mark and its outline.
            Place(overlaid.Scrim, Rect.zero);
        }

        private static void SetSurfaceCatchesClicks(bool catches)
        {
            if (_overlaid != null && _overlaid.Overlay.TryGetComponent<Image>(out var surface))
            {
                surface.raycastTarget = catches;
            }
        }

        public static void Disarm()
        {
            var overlaid = _overlaid;
            if (overlaid == null)
            {
                return;
            }

            var root = overlaid.Overlay.transform.parent.gameObject;

            _overlaid = null;
            _picking = false;
            IsPointerOverApp = false;

            UnityEngine.Object.DestroyImmediate(root);
        }

        /// <summary>
        ///     Marks <paramref name="screenRect"/> and outlines it.
        /// </summary>
        /// <remarks>
        ///     The feedback is on the widget, not in the surroundings, which is what a browser's element
        ///     picker does and is the only version with no degenerate case: marking the target works
        ///     whatever size it is, while dimming everything around it stops working entirely once the
        ///     target is the whole screen. The mark is light so the widget still shows in its own
        ///     colours, which is frequently the thing being looked at.
        /// </remarks>
        /// <param name="geometric">
        ///     Whether this was found by box rather than by raycast. Recolours the mark and the outline,
        ///     which is the only mode indicator anywhere near where the user is looking while picking.
        /// </param>
        public static void SetHighlight(Rect screenRect, bool geometric = false)
        {
            var overlaid = _overlaid;
            if (overlaid == null)
            {
                return;
            }

            var borderColour = geometric ? GeometricBorderColour : BorderColour;
            for (var i = 0; i < Bands; i++)
            {
                if (overlaid.BorderImage[i].color != borderColour)
                {
                    overlaid.BorderImage[i].color = borderColour;
                }
            }

            var fillColour = Fill(borderColour);
            if (overlaid.FillImage.color != fillColour)
            {
                overlaid.FillImage.color = fillColour;
            }

            // Everything below works in the canvas's own units, not in screen pixels. They coincide
            // for an unscaled overlay canvas and stop coinciding the moment anything is not, and a wash
            // that is a few percent short leaves an untinted strip down the edge of the screen.
            var size = overlaid.CanvasRect.rect.size;
            var min = ToCanvas(overlaid.CanvasRect, screenRect.min);
            var max = ToCanvas(overlaid.CanvasRect, screenRect.max);

            var w = size.x;
            var h = size.y;

            var left = Mathf.Clamp(min.x, 0, w);
            var right = Mathf.Clamp(max.x, 0, w);
            var bottom = Mathf.Clamp(min.y, 0, h);
            var top = Mathf.Clamp(max.y, 0, h);

            // Only while hunting: once something is picked the app is interactive again and a wash over
            // it would be a lie about who owns the input.
            Place(overlaid.Scrim, _picking ? new Rect(Vector2.zero, size) : Rect.zero);
            Place(overlaid.Fill, Rect.MinMaxRect(left, bottom, right, top));

            // Drawn INSIDE the box, because the box is routinely flush with a screen edge and an
            // outside stroke there is simply off-screen -- a full-height drawer loses three of its four
            // sides that way, and a full-screen widget loses all four.
            var t = BorderThickness;
            Place(overlaid.Border[0], Rect.MinMaxRect(left, top - t, right, top));
            Place(overlaid.Border[1], Rect.MinMaxRect(left, bottom, right, bottom + t));
            Place(overlaid.Border[2], Rect.MinMaxRect(left, bottom, left + t, top));
            Place(overlaid.Border[3], Rect.MinMaxRect(right - t, bottom, right, top));
        }

        /// <summary>Nothing marked: the wash alone while picking, and nothing at all once it has stopped.</summary>
        public static void ClearHighlight()
        {
            var overlaid = _overlaid;
            if (overlaid == null)
            {
                return;
            }

            Place(
                overlaid.Scrim,
                _picking ? new Rect(Vector2.zero, overlaid.CanvasRect.rect.size) : Rect.zero
            );
            Place(overlaid.Fill, Rect.zero);

            for (var i = 0; i < Bands; i++)
            {
                Place(overlaid.Border[i], Rect.zero);
            }
        }

        /// <summary>The mark laid over the widget: the outline's colour, at a fraction of its weight.</summary>
        private static Color Fill(Color border) =>
            new Color(border.r, border.g, border.b, FillAlpha);

        /// <summary>A screen point in canvas units, measured from the canvas's bottom-left corner.</summary>
        private static Vector2 ToCanvas(RectTransform canvasRect, Vector2 screenPoint)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPoint,
                null,
                out var local
            );

            // Local space is measured from the pivot, which sits in the middle. Everything else here
            // places pieces from the bottom-left, so rebase once and be done with it.
            return local - canvasRect.rect.min;
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

        private sealed class Overlay
            : MonoBehaviour,
                IPointerClickHandler,
                IPointerMoveHandler,
                IPointerExitHandler
        {
            public void OnPointerMove(PointerEventData eventData)
            {
                IsPointerOverApp = true;
                Hovered?.Invoke(eventData.position);
            }

            // Hands the highlight back rather than clearing it. Clearing here is what made a tree
            // selection appear to need a round trip: moving the pointer from the Game view to the
            // window crosses this boundary, so the selection's highlight was wiped on the way over.
            public void OnPointerExit(PointerEventData eventData)
            {
                IsPointerOverApp = false;
                Exited?.Invoke();
            }

            // A click ends the hunt: the listener calls StopPicking, which is the expectation a browser's
            // element picker sets. Until then the app receives nothing, which is what the toggle is for.
            public void OnPointerClick(PointerEventData eventData) =>
                Picked?.Invoke(eventData.position);
        }
    }
}
#endif
