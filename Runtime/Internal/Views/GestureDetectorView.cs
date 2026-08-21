using System;
using UniMob.UI;
using UniMob.UI.Internal;
using UniMob.UI.Internal.Views;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.EventSystems;

[assembly: RegisterComponentViewFactory(
    "UniMob.GestureDetectorView",
    typeof(RectTransform),
    typeof(InvisibleRaycastTarget),
    typeof(GestureDetectorView)
)]

namespace UniMob.UI.Internal.Views
{
    internal class GestureDetectorView : SingleChildLayoutView<IGestureDetectorState>
    {
        /// <summary>
        /// Whether this detector takes the pointer press, and not only what it was asked for. A tap
        /// handler does, because a tap is the end of a press: leaving the press to be claimed by
        /// whatever sits above -- a button wrapping this detector -- presses that ancestor for a
        /// gesture that is not its own, and costs the tap outright under an input module that
        /// requires one object to own both ends. A drag-only detector claims nothing, so the button
        /// above it keeps working.
        /// </summary>
        internal static bool HandlesPress(IGestureDetectorState state) =>
            state.OnTap != null || state.OnPointerDown != null || state.OnPointerUp != null;

        protected override void Activate()
        {
            base.Activate();

            var target = GetComponent<InvisibleRaycastTarget>();
            target.raycastTarget = true;
        }

        private GestureDetectorTapReceiver? _tapReceiver;
        private GestureDetectorPressReceiver? _pressReceiver;
        private GestureDetectorDragReceiver? _dragReceiver;
        private GestureDetectorMoveReceiver? _moveReceiver;

        protected override void Render()
        {
            base.Render();
            if (State == null)
                return;

            // 1. Manage TAP
            if (State.OnTap != null)
            {
                if (_tapReceiver == null)
                    _tapReceiver = gameObject.AddComponent<GestureDetectorTapReceiver>();
                _tapReceiver.OnTap = State.OnTap;
            }
            else if (_tapReceiver != null)
            {
                Destroy(_tapReceiver);
                _tapReceiver = null;
            }

            // 2. Manage PRESS (Down/Up)
            if (HandlesPress(State))
            {
                if (_pressReceiver == null)
                    _pressReceiver = gameObject.AddComponent<GestureDetectorPressReceiver>();
                _pressReceiver.OnPointerDownDelegate = State.OnPointerDown;
                _pressReceiver.OnPointerUpDelegate = State.OnPointerUp;
            }
            else if (_pressReceiver != null)
            {
                Destroy(_pressReceiver);
                _pressReceiver = null;
            }

            // Manage MOVE
            if (State.OnPointerMove != null)
            {
                if (_moveReceiver == null)
                    _moveReceiver = gameObject.AddComponent<GestureDetectorMoveReceiver>();
                _moveReceiver.OnMove = State.OnPointerMove;
            }
            else if (_moveReceiver != null)
            {
                Destroy(_moveReceiver);
                _moveReceiver = null;
            }

            // 3. Manage DRAG
            if (State.OnDragUpdate != null)
            {
                if (_dragReceiver == null)
                    _dragReceiver = gameObject.AddComponent<GestureDetectorDragReceiver>();
                _dragReceiver.OnDragUpdate = State.OnDragUpdate;
            }
            else if (_dragReceiver != null)
            {
                Destroy(_dragReceiver);
                _dragReceiver = null;
            }
        }
    }

    /// <summary>
    /// What every gesture receiver needs: the canvas its screen points are measured against.
    /// </summary>
    public abstract class GestureDetectorReceiver : UIBehaviour
    {
        private Canvas? canvas;

        /// <summary>
        /// Resolved when a gesture arrives rather than when the component is created. A view is
        /// pooled and reparented, so the canvas above a receiver at construction is not always the
        /// canvas above it at the tap, and at construction there may be none at all.
        /// </summary>
        protected Canvas? Canvas =>
            this.canvas != null ? this.canvas : this.canvas = this.GetComponentInParent<Canvas>();

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            this.canvas = null;
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            this.canvas = null;
        }

        /// <summary>
        /// The two points a gesture carries: inside this receiver's own box, and in the canvas. Both
        /// read zero when there is no canvas to measure against -- which a pointer event should not
        /// be able to arrive without, so it is reported rather than costing the gesture.
        /// </summary>
        protected (Vector2 Local, Vector2 Global) PointsOf(Vector2 screenPoint)
        {
            var resolved = this.Canvas;
            if (resolved == null)
            {
                this.ReportMissingCanvas();
                return (Vector2.zero, Vector2.zero);
            }

            return (
                CoordinateUtils.GetLocalPosition(
                    screenPoint,
                    (RectTransform)this.transform,
                    resolved.worldCamera
                ),
                CoordinateUtils.GetGlobalPosition(screenPoint, resolved)
            );
        }

        /// <summary>Pointer movement in canvas units; zero without a canvas, as <see cref="PointsOf"/>.</summary>
        protected Vector2 LogicalDeltaOf(Vector2 screenDelta)
        {
            var resolved = this.Canvas;
            if (resolved == null)
            {
                this.ReportMissingCanvas();
                return Vector2.zero;
            }

            return CoordinateUtils.GetLogicalDelta(screenDelta, resolved);
        }

        private void ReportMissingCanvas() =>
            Debug.LogError(
                $"'{this.name}' received a gesture with no Canvas above it; its position reads as zero.",
                this
            );
    }

    public class GestureDetectorTapReceiver : GestureDetectorReceiver, IPointerClickHandler
    {
        public Action<TapDetails>? OnTap;

        public void OnPointerClick(PointerEventData eventData) => OnTap?.Invoke(Convert(eventData));

        private TapDetails Convert(PointerEventData eventData)
        {
            var (localPosition, globalPosition) = this.PointsOf(eventData.position);
            return new TapDetails
            {
                LocalPosition = localPosition,
                GlobalPosition = globalPosition,
                PointerId = eventData.pointerId,
                Pressure = eventData.pressure,
                Timestamp = DateTimeOffset.Now,
                Size = ((RectTransform)this.transform).rect.size,
            };
        }
    }

    public class GestureDetectorMoveReceiver : GestureDetectorReceiver, IPointerMoveHandler
    {
        public Action<PointerDetails>? OnMove;

        public void OnPointerMove(PointerEventData eventData) => OnMove?.Invoke(Convert(eventData));

        private PointerDetails Convert(PointerEventData eventData)
        {
            var (localPosition, globalPosition) = this.PointsOf(eventData.position);
            return new PointerDetails
            {
                LocalPosition = localPosition,
                GlobalPosition = globalPosition,
                PointerId = eventData.pointerId,
                Pressure = eventData.pressure,
                Timestamp = DateTimeOffset.Now,
                Size = ((RectTransform)this.transform).rect.size,
            };
        }
    }

    // --- PRESS RECEIVER ---
    public class GestureDetectorPressReceiver
        : GestureDetectorReceiver,
            IPointerDownHandler,
            IPointerUpHandler
    {
        public Action<PointerDetails>? OnPointerDownDelegate;
        public Action<PointerDetails>? OnPointerUpDelegate;

        public void OnPointerDown(PointerEventData eventData) =>
            OnPointerDownDelegate?.Invoke(Convert(eventData));

        public void OnPointerUp(PointerEventData eventData) =>
            OnPointerUpDelegate?.Invoke(Convert(eventData));

        private PointerDetails Convert(PointerEventData eventData)
        {
            var (localPosition, globalPosition) = this.PointsOf(eventData.position);
            return new PointerDetails
            {
                LocalPosition = localPosition,
                GlobalPosition = globalPosition,
                PointerId = eventData.pointerId,
                Pressure = eventData.pressure,
                Timestamp = DateTimeOffset.Now,
                Size = ((RectTransform)this.transform).rect.size,
            };
        }
    }

    // --- DRAG RECEIVER ---
    public class GestureDetectorDragReceiver
        : GestureDetectorReceiver,
            IBeginDragHandler,
            IDragHandler,
            IEndDragHandler
    {
        public Action<DragDetails>? OnDragBegin;
        public Action<DragDetails>? OnDragEnd;
        public Action<DragDetails>? OnDragUpdate;

        // Unity requires Begin/End to exist for IDragHandler to play nicely with ScrollRects
        public void OnBeginDrag(PointerEventData eventData) =>
            OnDragBegin?.Invoke(Convert(eventData));

        public void OnEndDrag(PointerEventData eventData) => OnDragEnd?.Invoke(Convert(eventData));

        public void OnDrag(PointerEventData eventData) => OnDragUpdate?.Invoke(Convert(eventData));

        private DragDetails Convert(PointerEventData eventData)
        {
            // 1. Calculate the points
            var (localPosition, globalPosition) = this.PointsOf(eventData.position);
            // 2. Calculate the deltas
            var logicalDelta = this.LogicalDeltaOf(eventData.delta);

            return new DragDetails
            {
                GlobalPosition = globalPosition,
                LocalPosition = localPosition,

                // Since deltas are just directional distances, Global and Local deltas
                // are identical unless the widget is rotated or scaled.
                GlobalDelta = logicalDelta,
                LocalDelta = logicalDelta,

                PointerId = eventData.pointerId,
                Pressure = eventData.pressure,
                Timestamp = DateTimeOffset.Now,

                Size = ((RectTransform)this.transform).rect.size,
            };
        }
    }

    internal static class CoordinateUtils
    {
        /// <summary>
        /// Converts a physical screen point into a Flutter-style Local coordinate.
        /// Result: (0,0) is exactly the Top-Left of the widget, Y goes down.
        /// </summary>
        public static Vector2 GetLocalPosition(
            Vector2 screenPos,
            RectTransform widgetRect,
            Camera? camera
        )
        {
            // 1. Get the raw point relative to the Widget's pivot (handles Canvas scaling automatically!)
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                widgetRect,
                screenPos,
                camera,
                out Vector2 rawLocal
            );

            // 2. Convert Unity Pivot/Y-Up to Flutter Top-Left/Y-Down
            float x = rawLocal.x + (widgetRect.pivot.x * widgetRect.rect.width);
            float y = (widgetRect.pivot.y * widgetRect.rect.height) - rawLocal.y;

            return new Vector2(x, y);
        }

        /// <summary>
        /// Converts a physical screen point into a Flutter-style Global coordinate.
        /// Result: (0,0) is exactly the Top-Left of the entire Screen/Canvas, Y goes down.
        /// </summary>
        public static Vector2 GetGlobalPosition(Vector2 screenPos, Canvas canvas)
        {
            var canvasRect = (RectTransform)canvas.transform;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPos,
                canvas.worldCamera,
                out Vector2 rawLocal
            );

            float x = rawLocal.x + (canvasRect.pivot.x * canvasRect.rect.width);
            float y = (canvasRect.pivot.y * canvasRect.rect.height) - rawLocal.y;

            return new Vector2(x, y);
        }

        /// <summary>
        /// Converts physical screen delta to logical delta.
        /// Result: Dragging right is +X, dragging down is +Y.
        /// </summary>
        public static Vector2 GetLogicalDelta(Vector2 screenDelta, Canvas canvas)
        {
            return new Vector2(
                screenDelta.x / canvas.scaleFactor,
                -screenDelta.y / canvas.scaleFactor
            );
        }
    }
}
