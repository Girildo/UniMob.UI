using System;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public interface IPositionedBoxState : ISingleChildLayoutState
    {
        Alignment Alignment { get; }
        float? WidthFactor { get; }
        float? HeightFactor { get; }
    }

    /// <summary>
    /// A render object that positions a child within itself, potentially creating
    /// more space for alignment by expanding or applying size factors.
    /// It inherits common child management from SingleChildRenderObject.
    /// </summary>
    public class RenderPositionedBox : SingleChildRenderObject
    {
        private readonly IPositionedBoxState _state;

        public RenderPositionedBox(IPositionedBoxState state) : base(state)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            //We let the child be as small as it wants, so we loosen the constraints
            if (Child != null)
                ChildSize = LayoutChild(Child, constraints.Loosen());
            else
                ChildSize = Vector2.zero;


            // Determine if we should HUG our content or expand to fill the available space.
            var shrinkWrapWidth = _state.WidthFactor.HasValue || !constraints.HasBoundedWidth;
            var shrinkWrapHeight = _state.HeightFactor.HasValue || !constraints.HasBoundedHeight;

            var selfWidth = shrinkWrapWidth ? ChildSize.x * (_state.WidthFactor ?? 1f) : float.PositiveInfinity;
            var selfHeight = shrinkWrapHeight ? ChildSize.y * (_state.HeightFactor ?? 1f) : float.PositiveInfinity;

            // Finally, constrain the calculated size to the parent's limits.
            return constraints.Constrain(new Vector2(selfWidth, selfHeight));
        }


        protected override void PerformPositioning()
        {
            if (Child == null)
            {
                ChildPosition = Vector2.zero;
                return;
            }

            // Calculate the top-left corner of the child based on the alignment
            // and the available space (Size) versus the child's size (ChildSize).
            ChildPosition = _state.Alignment.ResolveOffset(Size, ChildSize);
        }
    }
}