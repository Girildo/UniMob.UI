using System;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public class RenderIntrinsicSize : RenderObject
    {
        private readonly IntrinsicSizeState _state;

        private IntrinsicSize Widget => (IntrinsicSize) _state.RawWidget;

        public RenderIntrinsicSize(IntrinsicSizeState state) : base(state.StateLifetime)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            var widget = this.Widget;

            var childConstraints = widget.Axis == Axis.Horizontal
                ? constraints.Tighten(width: GetIntrinsicWidth(float.PositiveInfinity))
                : constraints.Tighten(height: GetIntrinsicHeight(float.PositiveInfinity));

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