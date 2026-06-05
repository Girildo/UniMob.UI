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
                //var children = State.Screens;
                //foreach (var child in children)
                //{
                //    var childView = render.RenderItem(child);

                //    if (child.RenderObject is Layout.Internal.RenderObjects.RenderLegacy)
                //    {
                //        var childSize = child.Size;

                //        LayoutData layout;
                //        layout.Size = childSize.GetSizeUnbounded();
                //        layout.Alignment = Alignment.Center;
                //        layout.Corner = Alignment.Center;
                //        layout.CornerPosition = Vector2.zero;
                //        ViewLayoutUtility.SetLayout(childView.rectTransform, layout);
                //    }
                //    else
                //    {
                //        // 1. Pass down the screen constraints
                //        state.UpdateConstraints(screenConstraints);

                //        // 2. Perform the C# layout pass
                //        var size = state.WatchedPerformLayout();

                //        // 3. Sync the View's RectTransform to match the RenderObject's top-left coordinate space
                //        var rt = childView.rectTransform;
                //        rt.anchorMin = new Vector2(0, 1);
                //        rt.anchorMax = new Vector2(0, 1);
                //        rt.pivot = new Vector2(0, 1);
                //        rt.sizeDelta = size;
                //        rt.anchoredPosition = Vector2.zero;
                //    }
                //}
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
                        // 1. Pass down the screen constraints
                        state.UpdateConstraints(screenConstraints);

                        // 2. Perform the C# layout pass
                        var size = ((IState) state).WatchedPerformLayout();

                        // 3. Sync the View's RectTransform to match the RenderObject's top-left coordinate space
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