using System;

namespace UniMob.UI
{
    public interface IGestureDetectorState : ISingleChildLayoutState
    {
        Action<TapDetails>? OnTap { get; }
        Action<PointerDetails>? OnPointerDown { get; }
        Action<PointerDetails>? OnPointerUp { get; }
        Action<DragDetails>? OnDragUpdate { get; }
        Action<PointerDetails>? OnPointerMove { get; }
    }
}
