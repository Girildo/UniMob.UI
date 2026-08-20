using UnityEngine;

namespace UniMob.UI.Rendering
{
    public class RenderPadding : SingleChildRenderObject, ISingleChildRenderObject
    {
        private readonly IPaddingState _state;

        public RenderPadding(IPaddingState state)
            : base(state)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            var padding = _state.Padding;

            if (_state.Child == null)
            {
                // If there is no child, the size is simply the padding size.
                var empty = new Vector2(padding.Horizontal, padding.Vertical);
                ReportContentOverflow(constraints, empty, TrimThePadding);
                return constraints.Constrain(empty);
            }

            var innerConstraints = constraints.Deflate(padding);

            ChildSize = LayoutChild(_state.Child, innerConstraints);

            // Deflate floors the inner maximum at zero, so padding wider than the box does not hand
            // the child a negative one -- it squeezes the child out and leaves the padding itself
            // overflowing. That is the shape this reports; when the padding does fit, the child was
            // bounded by the deflated maximum and the sum cannot exceed the original.
            var desired = new Vector2(
                ChildSize.x + padding.Horizontal,
                ChildSize.y + padding.Vertical
            );

            ReportContentOverflow(constraints, desired, TrimThePadding);

            // Ensure the final size still respects the original parent constraints.
            return constraints.Constrain(desired);
        }

        private const string TrimThePadding =
            "The child plus its padding needs more room than this box allows. Reduce the padding, or "
            + "give the Padding a larger box.";

        protected override void PerformPositioning(Vector2 size)
        {
            var padding = _state.Padding;

            // The child is simply positioned at an offset equal to the top-left padding.
            ChildPosition = new Vector2(padding.Left, padding.Top);
        }

        protected override float ComputeIntrinsicWidth(float height)
        {
            var padding = _state.Padding;
            var childIntrinsicWidth = base.ComputeIntrinsicWidth(height - padding.Vertical);
            return childIntrinsicWidth + padding.Horizontal;
        }

        protected override float ComputeIntrinsicHeight(float width)
        {
            var padding = _state.Padding;
            var childIntrinsicHeight = base.ComputeIntrinsicHeight(width - padding.Horizontal);
            return childIntrinsicHeight + padding.Vertical;
        }
    }
}
