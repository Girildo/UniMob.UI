// In a new file: Runtime/Layout/Internal/RenderObjects/RenderPadding.cs

using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    internal class RenderPadding : SingleChildRenderObject, ISingleChildRenderObject
    {
        private readonly IPaddingState _state;

        public RenderPadding(IPaddingState state) : base(state)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            var padding = _state.Padding;

            if(_state.Child == null)
            {
                // If there is no child, the size is simply the padding size.
                var width = padding.Horizontal;
                var height = padding.Vertical;
                return constraints.Constrain(new Vector2(width, height));
            }

            // 1. Deflate the parent's constraints by the padding amount.
            // This creates the smaller box in which the child can be laid out.
            var innerConstraints = constraints.Deflate(padding);

            // 2. Lay out the child within those smaller, inner constraints.
            ChildSize = LayoutChild(_state.Child, innerConstraints);

            // 3. The final size of the Padding widget is the child's size
            // plus the padding on all sides.
            var finalWidth = ChildSize.x + padding.Horizontal;
            var finalHeight = ChildSize.y + padding.Vertical;

            // Ensure the final size still respects the original parent constraints.
            return constraints.Constrain(new Vector2(finalWidth, finalHeight));
        }

        protected override void PerformPositioning()
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