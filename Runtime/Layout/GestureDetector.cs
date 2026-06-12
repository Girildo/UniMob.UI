#nullable enable
using System;
using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Layout.Internal.Views;
using UnityEngine;

namespace UniMob.UI.Layout
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
        public Action<TapDetails>? OnTap { get; set; }
        public Action<PointerDetails>? OnPointerDown { get; set; }
        public Action<PointerDetails>? OnPointerUp { get; set; }
        public Action<PointerDetails>? OnPointerMove { get; set; }
        public Action<DragDetails>? OnDragUpdate { get; set; }
        public HitTestBehavior Behavior { get; set; } = HitTestBehavior.DeferToChild;

        public override State CreateState() => new GestureDetectorState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderConstrainedBox((GestureDetectorState) state);
        }
    }

    public enum HitTestBehavior
    {
        // Sizes exactly to the child's mathematical bounds
        DeferToChild,
        // Expands to fill all available space in its parent
        Opaque
    }



    public class GestureDetectorState : SingleChildLayoutState<GestureDetector>, IGestureDetectorState
    {
        public Action<TapDetails>? OnTap => Widget.OnTap;
        public Action<PointerDetails>? OnPointerDown => Widget.OnPointerDown;

        public Action<PointerDetails>? OnPointerUp => Widget.OnPointerUp;
        public Action<PointerDetails>? OnPointerMove => Widget.OnPointerMove;

        public Action<DragDetails>? OnDragUpdate => Widget.OnDragUpdate;

        public override WidgetViewReference View => WidgetViewReference.Resource("$$_Layout.GestureDetector");

        [Atom]
        public LayoutConstraints BoxConstraints
        {
            get
            {
                if (this.Widget.Behavior == HitTestBehavior.Opaque)
                {
                    return LayoutConstraints.Expanded();
                }
                else
                {
                    return LayoutConstraints.Unbounded();
                }
            }
        }
    }
}