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
            this._state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            var selfConstraints = _state.BoxConstraints;
            var childConstraints = selfConstraints.Enforce(constraints);

            // If there is no child, we size ourself to the smallest size allowed by the child constraints.
            if (_state.Child != null)
            {
                // Constrained, not forwarded verbatim: a child's size is only meaningful within the
                // constraints it was handed, and a box that named a tight height has to report that
                // height whatever the child answers. Forwarding raw let a child's violation through
                // the very box meant to bound it -- a SizedBox with a tight 84 could report otherwise.
                //
                // An unbounded axis is deliberately left alone here. There is no size to clamp an
                // infinity to, and quietly substituting one turns a broken layout into a silently
                // zero-sized widget; leaving it lets the view boundary report it against the widget
                // that actually asked for the impossible.
                ChildSize = LayoutChild(_state.Child, childConstraints);
                return ChildSize = childConstraints.Constrain(ChildSize);
            }

            // Constraining Vector2.zero mathematically guarantees we return the MinWidth/MinHeight.
            var width = childConstraints.MinWidth;
            var height = childConstraints.MinHeight;

            // If the box was asked to expand (Infinity), but the parent ALSO gave infinite space,
            // we must collapse back to the parent's safest minimum bound (usually 0) to avoid crashing Unity.
            if (float.IsPositiveInfinity(width)) width = constraints.MinWidth;
            if (float.IsPositiveInfinity(height)) height = constraints.MinHeight;

            return new Vector2(width, height);
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