using System;
using UniMob.UI.Internal.Views;
using UniMob.UI.Rendering;
using UnityEngine;

namespace UniMob.UI.Widgets
{
    public class GestureDetector : SingleChildLayoutWidget
    {
        // Gestures

        /// <summary>
        /// Called when this detector is tapped, with where the tap landed.
        /// </summary>
        /// <remarks>
        /// Handling a tap also takes the pointer PRESS, so a tap here never presses an ancestor
        /// button and arrives whichever input module the app installs. Nesting is still not a way to
        /// share a gesture: the innermost handler takes it and the widget above receives nothing.
        /// </remarks>
        public Action<TapDetails>? OnTap { get; init; }
        public Action<PointerDetails>? OnPointerDown { get; init; }
        public Action<PointerDetails>? OnPointerUp { get; init; }
        public Action<PointerDetails>? OnPointerMove { get; init; }

        /// <summary>
        /// Called when a drag on this detector begins, before the first <see cref="OnDragUpdate"/>.
        /// </summary>
        public Action<DragDetails>? OnDragStart { get; init; }

        public Action<DragDetails>? OnDragUpdate { get; init; }

        /// <summary>
        /// Called when the pointer that was dragging this detector is released.
        /// </summary>
        public Action<DragDetails>? OnDragEnd { get; init; }

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

        public Action<DragDetails>? OnDragStart => Widget.OnDragStart;

        public Action<DragDetails>? OnDragUpdate => Widget.OnDragUpdate;

        public Action<DragDetails>? OnDragEnd => Widget.OnDragEnd;

        public override WidgetViewReference View =>
            WidgetViewReference.Registered("UniMob.GestureDetectorView");
    }
}
