using System;
using UniMob.UI.Internal;
using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Layout.Internal.Views;
using UnityEngine;

[assembly: RegisterComponentViewFactory("$$_Layout.MultiChildLayoutView",
    typeof(RectTransform),
    typeof(MultiChildLayoutView))]

namespace UniMob.UI.Layout.Internal.Views
{
    public interface IMultiChildLayoutState : IState
    {
        IState[] Children { get; }
    }

    public class MultiChildLayoutView : View<IMultiChildLayoutState>
    {
        private ViewMapperBase _mapper;

        protected override void Awake()
        {
            base.Awake();
            _mapper = new PooledViewMapper(transform);
        }

        protected override void Render()
        {
#if UNITY_EDITOR
            this.name = $"MultiChildLayoutView [{this.State.RawWidget.GetType().Name}]";
#endif

            if (State.RenderObject is not IMultiChildrenRenderObject multiChildRenderObject)
                return;

            var childrenLayout = multiChildRenderObject.ChildrenLayout;

            using var render = _mapper.CreateRender();
            // Render each child
            for (var i = 0; i < State.Children.Length; i++)
            {
                var child = State.Children[i];

                if (child is null)
                {
                    throw new InvalidOperationException("Child state at position " + i + " is null. All children must have a valid state." +
                        "Use Empty if necessary.");
                }

                var layoutData = childrenLayout[i];

                if (float.IsInfinity(layoutData.Size.x))
                    throw new InvalidOperationException(
                        $"Child {child.GetType().Name} at position {i} has an unbounded width. " +
                        "This is not supported in MultiChildLayoutView. " +
                        "Try wrapping it in a Container or similar widget to constrain its width.");

                if (float.IsInfinity(layoutData.Size.y))
                    throw new InvalidOperationException(
                        $"Child {child.GetType().Name} at position {i} has an unbounded height. " +
                        "This is not supported in MultiChildLayoutView. " +
                        "Try wrapping it in a Container or similar widget to constrain its height.");


                var childView = render.RenderItem(child);
                var rt = childView.rectTransform;

                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(0, 1);

                var pivotOffset = new Vector2(
                    layoutData.Size.x * rt.pivot.x,
                    -layoutData.Size.y * (1.0f - rt.pivot.y)
                );

                rt.sizeDelta = layoutData.Size;
                if (!layoutData.CornerPosition.HasValue)
                    throw new InvalidOperationException(
                        $"LayoutData for child {i} does not have a CornerPosition set. This is required for positioning.");
                rt.anchoredPosition =
                    new Vector2(layoutData.CornerPosition.Value.x, -layoutData.CornerPosition.Value.y) + pivotOffset;
            }
        }
    }
}