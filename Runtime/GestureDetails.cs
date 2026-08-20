using System;
using UnityEngine;

namespace UniMob.UI
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
}
