using System.Collections.Generic;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public class RenderZStack : MultiChildRenderObject
    {
        private readonly IZStackState _state;


        public RenderZStack(IZStackState state) : base(state)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            ChildrenLayoutBuffer.Clear();
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
                    ChildrenLayoutBuffer.Add(new LayoutInfo());
                    continue;
                }

                // This is a non-positioned child.
                var childSize = LayoutChild(child, childConstraints);

                ChildrenLayoutBuffer.Add(new LayoutInfo { Size = childSize });

                maxWidth = Mathf.Max(maxWidth, childSize.x);
                maxHeight = Mathf.Max(maxHeight, childSize.y);
            }

            return constraints.Constrain(new Vector2(maxWidth, maxHeight));
        }

        protected override void PerformPositioning()
        {
            for (var i = 0; i < ChildrenLayoutBuffer.Count; i++)
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
            var childSize = ChildrenLayoutBuffer[index].Size;

            var layoutData = ChildrenLayoutBuffer[index];
            layoutData.Position = _state.Alignment.ResolveOffset(Size, childSize);
            ChildrenLayoutBuffer[index] = layoutData;
        }

        private void LayoutPositionedChild(int index, IState child, Positioned pos)
        {
            float? x = pos.Left;
            float? y = pos.Top;

            // An axis is tight where the child pins it -- an explicit extent, or both edges, which
            // together state a width -- and unbounded where it does not. Leaving the unpinned axis
            // unbounded is what lets a positioned child take its intrinsic size, so an overlay can be
            // larger than the box it is anchored to; bounding it to the stack instead would silently
            // clip exactly that case. A child that cannot size itself on an unbounded axis reports so
            // (see RenderFlex), which names the fix: pin the axis on the Positioned.
            var width =
                pos.Left != null && pos.Right != null
                    ? Size.x - pos.Left.Value - pos.Right.Value
                    : pos.Width;

            var height =
                pos.Top != null && pos.Bottom != null
                    ? Size.y - pos.Top.Value - pos.Bottom.Value
                    : pos.Height;

            var childConstraints = LayoutConstraints.TightFor(
                width: width.HasValue ? Mathf.Max(0f, width.Value) : null,
                height: height.HasValue ? Mathf.Max(0f, height.Value) : null
            );

            var childSize = LayoutChild(child, childConstraints);

            if (x == null)
            {
                x = Size.x - childSize.x - (pos.Right ?? 0);
            }

            if (y == null)
            {
                y = Size.y - childSize.y - (pos.Bottom ?? 0);
            }

            ChildrenLayoutBuffer[index] = new LayoutInfo { Size = childSize, Position = new Vector2(x.Value, y.Value) };
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