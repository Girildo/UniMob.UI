using System;

namespace UniMob.UI
{
    public interface IGestureDetectorState : ISingleChildLayoutState
    {
        Action<TapDetails>? OnTap { get; }
        Action<PointerDetails>? OnPointerDown { get; }
        Action<PointerDetails>? OnPointerUp { get; }
        Action<DragDetails>? OnDragStart { get; }
        Action<DragDetails>? OnDragUpdate { get; }
        Action<DragDetails>? OnDragEnd { get; }
        Action<PointerDetails>? OnPointerMove { get; }
    }
}
