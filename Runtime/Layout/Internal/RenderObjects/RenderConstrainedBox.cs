using System;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public interface IConstrainedBoxState : ISingleChildLayoutState
    {
        LayoutConstraints BoxConstraints { get; }
    }
    public class RenderConstrainedBox : RenderProxy
    {
        private readonly IConstrainedBoxState _state;

        public RenderConstrainedBox(IConstrainedBoxState state) : base(state)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            var selfConstraints = _state.BoxConstraints;
            var childConstraints = selfConstraints.Enforce(constraints);

            // If there is no child, we size ourself to the smallest size allowed by the child constraints.
            if (_state.Child == null)
                return childConstraints.Constrain(Vector2.zero);

            ChildSize = LayoutChild(_state.Child, childConstraints);
            return ChildSize;
        }


        // Intrinsic sizing is also delegated directly to the child, but constrained
        // by the BoxConstraints.
        protected override float ComputeIntrinsicWidth(float height)
        {
            var selfConstraints = _state.BoxConstraints;
            if (selfConstraints.HasTightWidth)
                return selfConstraints.MinWidth;
            
            var childIntrinsicWidth = base.ComputeIntrinsicWidth(height);
            return selfConstraints.ConstrainWidth(childIntrinsicWidth);
        }

        protected override float ComputeIntrinsicHeight(float width)
        {
            var selfConstraints = _state.BoxConstraints;
            if (selfConstraints.HasTightHeight)
                return selfConstraints.MinHeight;

            var childIntrinsicHeight = base.ComputeIntrinsicHeight(width);
            return selfConstraints.ConstrainHeight(childIntrinsicHeight);
        }

        
    }
}