using UniMob.UI;
using UniMob.UI.Internal;
using UniMob.UI.Navigation;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

[assembly: RegisterComponentViewFactory(
    "UniMob.NavigatorView",
    typeof(RectTransform),
    typeof(NavigatorView)
)]

namespace UniMob.UI.Navigation
{
    internal class NavigatorView : View<INavigatorState>
    {
        private ViewMapperBase _mapper = null!;

        protected override void Activate()
        {
            base.Activate();

            // link: false keeps each screen's rendering decoupled from the navigator's own render
            // pass, so deep changes inside one screen don't re-run the whole navigator.
            if (_mapper == null)
                _mapper = new PooledViewMapper(transform, link: false);
        }

        protected override void Render()
        {
            if (State.RenderObject is not IMultiChildrenRenderObject renderObject)
                return;

            var childrenLayout = renderObject.ChildrenLayout;
            var screens = State.Screens;

            using var render = _mapper.CreateRender();

            for (var i = 0; i < screens.Length; i++)
            {
                var child = screens[i];
                var layoutData = childrenLayout[i];

                var childView = render.RenderItem(child);
                var rt = childView.rectTransform;

                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(0, 1);

                var pivotOffset = new Vector2(
                    layoutData.Size.x * rt.pivot.x,
                    -layoutData.Size.y * (1.0f - rt.pivot.y)
                );

                rt.sizeDelta = layoutData.Size;
                rt.anchoredPosition =
                    new Vector2(layoutData.Position.x, -layoutData.Position.y) + pivotOffset;

                // Keep the Unity hierarchy order in sync with the declarative screen order.
                if (rt.GetSiblingIndex() != i)
                    rt.SetSiblingIndex(i);
            }
        }
    }

    public interface INavigatorState : IViewState
    {
        IState[] Screens { get; }
    }
}
