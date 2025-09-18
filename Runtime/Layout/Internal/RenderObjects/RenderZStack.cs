using System.Collections.Generic;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    internal class RenderZStack : RenderObject, IMultiChildRenderObject
    {
        private readonly ZStackState _state;

        private readonly List<LayoutData> _childrenLayout = new();
        public IReadOnlyList<LayoutData> ChildrenLayout => _childrenLayout;

        public ZStack Widget => (ZStack) _state.RawWidget;

        public RenderZStack(ZStackState state)
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
                var childWidget = (child as State)?.RawWidget;
                
                if (childWidget is Positioned)
                {
                    // Add a placeholder. The positioned child will be fully laid out later.
                    _childrenLayout.Add(new LayoutData());
                    continue;
                }

                // This is a non-positioned child.
                var childSize = LayoutChild(child, childConstraints);

                _childrenLayout.Add(new LayoutData { Size = childSize });
                
                maxWidth = Mathf.Max(maxWidth, childSize.x);
                maxHeight = Mathf.Max(maxHeight, childSize.y);
            }

            return new Vector2(
                Mathf.Clamp(maxWidth, constraints.MinWidth, constraints.MaxWidth),
                Mathf.Clamp(maxHeight, constraints.MinHeight, constraints.MaxHeight)
            );
        }

        protected override void PerformPositioning()
        {
            var widget = this.Widget;

            for (var i = 0; i < _childrenLayout.Count; i++)
            {
                var child = _state.Children[i];
                var childWidget = (child as State)?.RawWidget;
                
                if (childWidget is Positioned pos)
                {
                    LayoutPositionedChild(i, child, pos);
                }
                else
                {
                    LayoutNonPositionedChild(i);
                }
            }
        }

        private void LayoutNonPositionedChild(int index)
        {
            var alignment = Widget.Alignment;
            
            // Now this correctly retrieves the size calculated in *this* frame's sizing pass.
            var childSize = _childrenLayout[index].Size;

            var x = (Size.x - childSize.x) * (alignment.X * 0.5f + 0.5f);
            var y = (Size.y - childSize.y) * (alignment.Y * 0.5f + 0.5f);
            
            var layoutData = _childrenLayout[index];
            layoutData.CornerPosition = new Vector2(x, y);
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
            
            if (x == null)
            {
                x = Size.x - childSize.x - (pos.Right ?? 0);
            }

            if (y == null)
            {
                y = Size.y - childSize.y - (pos.Bottom ?? 0);
            }

            _childrenLayout[index] = new LayoutData { Size = childSize, CornerPosition = new Vector2(x.Value, y.Value) };
        }

        public override float GetIntrinsicWidth(float height)
        {
            float maxWidth = 0;
            foreach (var child in _state.Children)
            {
                if ((child as State)?.RawWidget is Positioned) continue;
                maxWidth = Mathf.Max(maxWidth, child.RenderObject.GetIntrinsicWidth(height));
            }
            return maxWidth;
        }

        public override float GetIntrinsicHeight(float width)
        {
            float maxHeight = 0;
            foreach (var child in _state.Children)
            {
                if ((child as State)?.RawWidget is Positioned) continue;
                maxHeight = Mathf.Max(maxHeight, child.RenderObject.GetIntrinsicHeight(width));
            }
            return maxHeight;
        }


        
    }
}