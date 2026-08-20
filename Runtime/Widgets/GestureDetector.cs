using System;
using UniMob.UI.Internal.Views;
using UniMob.UI.Rendering;
using UnityEngine;

namespace UniMob.UI.Widgets
{
    public abstract record GestureDetails
    {
        /// <summary>
        /// The unique identifier for the pointer.
        /// </summary>
        public int PointerId { get; set; }

        /// <summary>
        /// The time at which the gesture was detected.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// The size of the widget that received the gesture at the time of the event.
        /// </summary>
        public Vector2 Size { get; set; }

        /// <summary>
        /// Contact pressure reported by the device, passed through untouched from the event.
        /// Pens and pressure-sensitive touchscreens report a value in 0..1; devices that do not
        /// measure pressure, and input modules that do not fill the field, leave it at 0. Treat 0
        /// as "not reported" rather than "no contact".
        /// </summary>
        public float Pressure { get; set; }
    }

    public record TapDetails : GestureDetails
    {
        public Vector2 LocalPosition { get; set; }
        public Vector2 GlobalPosition { get; set; }
    }

    public record PointerDetails : GestureDetails
    {
        public Vector2 LocalPosition { get; set; }
        public Vector2 GlobalPosition { get; set; }
    }

    public record DragDetails : GestureDetails
    {
        public Vector2 LocalPosition { get; set; }
        public Vector2 GlobalPosition { get; set; }

        public Vector2 LocalDelta { get; set; }
        public Vector2 GlobalDelta { get; set; }
    }

    public class GestureDetector : SingleChildLayoutWidget
    {
        // Gestures
        public Action<TapDetails>? OnTap { get; init; }
        public Action<PointerDetails>? OnPointerDown { get; init; }
        public Action<PointerDetails>? OnPointerUp { get; init; }
        public Action<PointerDetails>? OnPointerMove { get; init; }
        public Action<DragDetails>? OnDragUpdate { get; init; }

        public override State CreateState() => new GestureDetectorState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderProxy((GestureDetectorState)state);
        }
    }

    public class GestureDetectorState
        : SingleChildLayoutState<GestureDetector>,
            IGestureDetectorState
    {
        public Action<TapDetails>? OnTap => Widget.OnTap;
        public Action<PointerDetails>? OnPointerDown => Widget.OnPointerDown;

        public Action<PointerDetails>? OnPointerUp => Widget.OnPointerUp;
        public Action<PointerDetails>? OnPointerMove => Widget.OnPointerMove;

        public Action<DragDetails>? OnDragUpdate => Widget.OnDragUpdate;

        public override WidgetViewReference View =>
            WidgetViewReference.Registered("UniMob.GestureDetectorView");
    }
}
