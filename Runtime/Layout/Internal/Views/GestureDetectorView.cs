#nullable enable
using System;
using UniMob.UI.Internal;
using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Layout.Internal.Utilities;
using UniMob.UI.Layout.Internal.Views;
using UnityEngine;
using UnityEngine.EventSystems;

[assembly: RegisterComponentViewFactory(
    "$$_Layout.GestureDetector",
    typeof(RectTransform),
    typeof(InvisibleRaycastTarget),
    typeof(GestureDetectorView)
)]

namespace UniMob.UI.Layout.Internal.Views
{
    internal class GestureDetectorView : SingleChildLayoutView<IGestureDetectorState>
    {
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
            if (State.OnPointerDown != null || State.OnPointerUp != null)
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

    public class GestureDetectorTapReceiver : UIBehaviour, IPointerClickHandler
    {
        public Action<TapDetails>? OnTap;
        private Canvas canvas;

        protected override void Awake()
        {
            canvas = this.GetComponentInParent<Canvas>();
        }

        public void OnPointerClick(PointerEventData eventData) => OnTap?.Invoke(Convert(eventData));

        private TapDetails Convert(PointerEventData eventData)
        {
            var localPosition = CoordinateUtils.GetLocalPosition(
                eventData.position,
                this.transform as RectTransform,
                canvas.worldCamera
            );
            var globalPosition = CoordinateUtils.GetGlobalPosition(eventData.position, canvas);
            return new TapDetails
            {
                LocalPosition = localPosition,
                GlobalPosition = globalPosition,
                PointerId = eventData.pointerId,
                Pressure = eventData.pressure,
                Timestamp = DateTimeOffset.Now,
                Size = (this.transform as RectTransform).rect.size,
            };
        }
    }

    public class GestureDetectorMoveReceiver : UIBehaviour, IPointerMoveHandler
    {
        public Action<PointerDetails>? OnMove;
        private Canvas canvas;

        protected override void Awake()
        {
            canvas = this.GetComponentInParent<Canvas>();
        }

        public void OnPointerMove(PointerEventData eventData) => OnMove?.Invoke(Convert(eventData));

        private PointerDetails Convert(PointerEventData eventData)
        {
            var localPosition = CoordinateUtils.GetLocalPosition(
                eventData.position,
                this.transform as RectTransform,
                canvas.worldCamera
            );
            var globalPosition = CoordinateUtils.GetGlobalPosition(eventData.position, canvas);
            return new PointerDetails
            {
                LocalPosition = localPosition,
                GlobalPosition = globalPosition,
                PointerId = eventData.pointerId,
                Pressure = eventData.pressure,
                Timestamp = DateTimeOffset.Now,
                Size = (this.transform as RectTransform).rect.size,
            };
        }
    }

    // --- PRESS RECEIVER ---
    public class GestureDetectorPressReceiver : UIBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public Action<PointerDetails>? OnPointerDownDelegate;
        public Action<PointerDetails>? OnPointerUpDelegate;
        private Canvas canvas;

        protected override void Awake()
        {
            this.canvas = this.GetComponentInParent<Canvas>();
        }

        public void OnPointerDown(PointerEventData eventData) =>
            OnPointerDownDelegate?.Invoke(Convert(eventData));

        public void OnPointerUp(PointerEventData eventData) =>
            OnPointerUpDelegate?.Invoke(Convert(eventData));

        private PointerDetails Convert(PointerEventData eventData)
        {
            var localPosition = CoordinateUtils.GetLocalPosition(
                eventData.position,
                this.transform as RectTransform,
                canvas.worldCamera
            );
            var globalPosition = CoordinateUtils.GetGlobalPosition(eventData.position, canvas);
            return new PointerDetails
            {
                LocalPosition = localPosition,
                GlobalPosition = globalPosition,
                PointerId = eventData.pointerId,
                Pressure = eventData.pressure,
                Timestamp = DateTimeOffset.Now,
                Size = (this.transform as RectTransform).rect.size,
            };
        }
    }

    // --- DRAG RECEIVER ---
    public class GestureDetectorDragReceiver
        : UIBehaviour,
            IBeginDragHandler,
            IDragHandler,
            IEndDragHandler
    {
        public Action<DragDetails>? OnDragBegin;
        public Action<DragDetails>? OnDragEnd;
        public Action<DragDetails>? OnDragUpdate;
        private Canvas canvas;

        protected override void Awake()
        {
            this.canvas = this.GetComponentInParent<Canvas>();
        }

        // Unity requires Begin/End to exist for IDragHandler to play nicely with ScrollRects
        public void OnBeginDrag(PointerEventData eventData) =>
            OnDragBegin?.Invoke(Convert(eventData));

        public void OnEndDrag(PointerEventData eventData) => OnDragEnd?.Invoke(Convert(eventData));

        public void OnDrag(PointerEventData eventData) => OnDragUpdate?.Invoke(Convert(eventData));

        private DragDetails Convert(PointerEventData eventData)
        {
            // 1. Calculate the points
            // Pass the raw screen position directly to both utilities
            var localPosition = CoordinateUtils.GetLocalPosition(
                eventData.position,
                this.transform as RectTransform,
                canvas.worldCamera
            );
            var globalPosition = CoordinateUtils.GetGlobalPosition(eventData.position, canvas);
            // 2. Calculate the deltas
            var logicalDelta = CoordinateUtils.GetLogicalDelta(eventData.delta, canvas);

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

                Size = (this.transform as RectTransform).rect.size,
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
            var canvasRect = canvas.transform as RectTransform;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPos,
                canvas.worldCamera,
                out Vector2 rawLocal
            );

            float x = rawLocal.x + (canvasRect!.pivot.x * canvasRect.rect.width);
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

    public interface IGestureDetectorState : ISingleChildLayoutState
    {
        Action<TapDetails>? OnTap { get; }
        Action<PointerDetails>? OnPointerDown { get; }
        Action<PointerDetails>? OnPointerUp { get; }
        Action<DragDetails>? OnDragUpdate { get; }
        Action<PointerDetails>? OnPointerMove { get; }
    }
}
