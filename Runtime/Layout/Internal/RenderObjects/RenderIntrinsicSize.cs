using System;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public class RenderIntrinsicSize : SingleChildRenderObject
    {
        private readonly IIntrinsicSizeState _state;

        public RenderIntrinsicSize(IIntrinsicSizeState state) : base(state)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            
            var childConstraints = _state.Axis == Axis.Horizontal
                ? constraints.Tighten(width: GetIntrinsicWidth(constraints.HasBoundedHeight ? constraints.MaxHeight : float.PositiveInfinity))
                : constraints.Tighten(height: GetIntrinsicHeight(constraints.HasBoundedWidth ? constraints.MaxWidth : float.PositiveInfinity));
            var laidOut = LayoutChild(_state.Child, childConstraints);
            return constraints.Constrain(laidOut);
        }

        protected override void PerformPositioning()
        {
        }

        protected override float ComputeIntrinsicWidth(float height)
        {
            return _state.Child.RenderObject.GetIntrinsicWidth(height);
        }

        protected override float ComputeIntrinsicHeight(float width)
        {
            return _state.Child.RenderObject.GetIntrinsicHeight(width);
        }
    }
}