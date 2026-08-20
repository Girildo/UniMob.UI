using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Rendering
{
    public class RenderIntrinsicSize : SingleChildRenderObject
    {
        private readonly IIntrinsicSizeState _state;

        public RenderIntrinsicSize(IIntrinsicSizeState state)
            : base(state)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            if (_state.Child == null)
            {
                ChildSize = Vector2.zero;
                return constraints.Constrain(Vector2.zero);
            }

            var childConstraints =
                _state.Axis == Axis.Horizontal
                    ? constraints.Tighten(
                        width: GetIntrinsicWidth(
                            constraints.HasBoundedHeight
                                ? constraints.MaxHeight
                                : float.PositiveInfinity
                        )
                    )
                    : constraints.Tighten(
                        height: GetIntrinsicHeight(
                            constraints.HasBoundedWidth
                                ? constraints.MaxWidth
                                : float.PositiveInfinity
                        )
                    );
            ChildSize = LayoutChild(_state.Child, childConstraints);
            return constraints.Constrain(ChildSize);
        }

        protected override void PerformPositioning(Vector2 size)
        {
            ChildPosition = Vector2.zero;
        }
    }
}
