using System.Collections.Generic;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public class RenderZStack : RenderObject, IMultiChildrenRenderObject
    {
        private readonly IZStackState _state;

        private readonly List<LayoutInfo> _childrenLayout = new();
        public IReadOnlyList<LayoutInfo> ChildrenLayout => _childrenLayout;

        public RenderZStack(IZStackState state) : base(state.StateLifetime)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            _childrenLayout.Clear();
            float maxWidth = 0f;
            float maxHeight = 0f;

            var childConstraints = constraints.Loosen();

            // SIZING PASS:
            // The size of the ZStack is determined ONLY by its non-positioned children.
            for (var i = 0; i < _state.Children.Length; i++)
            {
                var child = _state.Children[i];

                if (child.InnerViewState is PositionedState)
                {
                    // Add a placeholder. The positioned child will be fully laid out later.
                    _childrenLayout.Add(new LayoutInfo());
                    continue;
                }

                // This is a non-positioned child.
                var childSize = LayoutChild(child, childConstraints);

                _childrenLayout.Add(new LayoutInfo { Size = childSize });

                maxWidth = Mathf.Max(maxWidth, childSize.x);
                maxHeight = Mathf.Max(maxHeight, childSize.y);
            }

            return constraints.Constrain(new Vector2(maxWidth, maxHeight));
        }

        protected override void PerformPositioning()
        {
            for (var i = 0; i < _childrenLayout.Count; i++)
            {
                var child = _state.Children[i];
                if (child.InnerViewState is PositionedState pos)
                {
                    LayoutPositionedChild(i, child, pos.RawWidget as Positioned);
                }
                else
                {
                    LayoutNonPositionedChild(i);
                }
            }
        }

        private void LayoutNonPositionedChild(int index)
        {
            // Now this correctly retrieves the size calculated in *this* frame's sizing pass.
            var childSize = _childrenLayout[index].Size;

            var layoutData = _childrenLayout[index];
            layoutData.Position = _state.Alignment.ResolveOffset(Size, childSize);
            _childrenLayout[index] = layoutData;
        }

        private void LayoutPositionedChild(int index, IState child, Positioned pos)
        {
            float? x = pos.Left;
            float? y = pos.Top;

            var childConstraints = new LayoutConstraints(
                pos.Width ?? 0,
                pos.Height ?? 0,
                pos.Width ?? float.PositiveInfinity,
                pos.Height ?? float.PositiveInfinity
            );

            if (pos.Left != null && pos.Right != null)
            {
                childConstraints = childConstraints.Tighten(width: Size.x - pos.Left - pos.Right);
            }

            if (pos.Top != null && pos.Bottom != null)
            {
                childConstraints = childConstraints.Tighten(height: Size.y - pos.Top - pos.Bottom);
            }

            var childSize = LayoutChild(child, childConstraints);

            // ResolveOffset, rather than ToAnchor, because this position is y-DOWN from the stack's
            // top edge -- the same space LayoutNonPositionedChild resolves into. ToAnchor is the
            // y-up canvas convention and would invert the vertical shift.
            if (pos.ChildAnchor is { } childAnchor)
            {
                var shift = childAnchor.ResolveOffset(childSize, Vector2.zero);

                if (x != null)
                {
                    x -= shift.x;
                }

                if (y != null)
                {
                    y -= shift.y;
                }
            }

            if (x == null)
            {
                x = Size.x - childSize.x - (pos.Right ?? 0);
            }

            if (y == null)
            {
                y = Size.y - childSize.y - (pos.Bottom ?? 0);
            }

            _childrenLayout[index] = new LayoutInfo { Size = childSize, Position = new Vector2(x.Value, y.Value) };
        }

        protected override float ComputeIntrinsicWidth(float height)
        {
            float maxWidth = 0;
            foreach (var child in _state.Children)
            {
                if (child.InnerViewState is PositionedState) continue;
                maxWidth = Mathf.Max(maxWidth, child.RenderObject.GetIntrinsicWidth(height));
            }
            return maxWidth;
        }

        protected override float ComputeIntrinsicHeight(float width)
        {
            float maxHeight = 0;
            foreach (var child in _state.Children)
            {
                if (child.InnerViewState is PositionedState) continue;
                maxHeight = Mathf.Max(maxHeight, child.RenderObject.GetIntrinsicHeight(width));
            }
            return maxHeight;
        }
    }
}