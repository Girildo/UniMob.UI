using System;
using UniMob.UI.Internal.Views;
using UniMob.UI.Rendering;
using UnityEngine;

namespace UniMob.UI.Widgets
{
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
