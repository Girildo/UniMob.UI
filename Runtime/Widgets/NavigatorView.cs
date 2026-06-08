using UniMob.Core;
using UniMob.UI.Internal;
using UniMob.UI.Layout;
using UniMob.UI.Widgets;
using UnityEngine;

[assembly: RegisterComponentViewFactory("$$_Navigator",
    typeof(RectTransform), typeof(NavigatorView))]

namespace UniMob.UI.Widgets
{
    internal class NavigatorView : View<INavigatorState>
    {
        private ViewMapperBase _mapper;

        protected override void Activate()
        {
            base.Activate();

            if (_mapper == null)
                _mapper = new PooledViewMapper(transform, link: false);
        }

        protected override void Render()
        {
            using (var render = _mapper.CreateRender())
            {
                var children = State.Screens;

                // Get the available screen space from the Navigator's own RectTransform
                var myRect = ((RectTransform) transform).rect;
                var screenConstraints = LayoutConstraints.Tight(myRect.width, myRect.height);

                foreach (var child in children)
                {
                    var childView = render.RenderItem(child);

                    // BRIDGE: Check if the child is a Modern Layout Widget
                    if (child is State state && state.RenderObject is not Layout.Internal.RenderObjects.RenderLegacy)
                    {
                        
                        state.UpdateConstraints(screenConstraints);
                        
                        var size = ((IState) state).WatchedPerformLayout();

                        var rt = childView.rectTransform;
                        rt.anchorMin = new Vector2(0, 1);
                        rt.anchorMax = new Vector2(0, 1);
                        rt.pivot = new Vector2(0, 1);
                        rt.sizeDelta = size;
                        rt.anchoredPosition = Vector2.zero;
                    }
                    else
                    {
                        // LEGACY PATH: Fall back to the old layout logic
                        var childSize = child.Size;
                        LayoutData layout;
                        layout.Size = childSize.GetSizeUnbounded();
                        layout.Alignment = Alignment.Center;
                        layout.Corner = Alignment.Center;
                        layout.CornerPosition = Vector2.zero;
                        ViewLayoutUtility.SetLayout(childView.rectTransform, layout);
                    }
                }
            }
        }
    }

    public interface INavigatorState : IViewState
    {
        IState[] Screens { get; }
    }
}