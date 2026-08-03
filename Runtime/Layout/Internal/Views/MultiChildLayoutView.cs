using System;
using UniMob.UI.Internal;
using UniMob.UI.Layout.Internal.Diagnostics;
using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Layout.Internal.Views;
using UnityEngine;

[assembly: RegisterComponentViewFactory("$$_Layout.MultiChildLayoutView",
    typeof(RectTransform),
    typeof(MultiChildLayoutView))]

namespace UniMob.UI.Layout.Internal.Views
{
    /// <summary>
    ///     A state that presents an ordered set of children to the layout system. Like
    ///     <see cref="ISingleChildLayoutState"/>, this is only the child link: geometry is read off the
    ///     render object, and on-screen geometry belongs to <see cref="IViewState"/>.
    /// </summary>
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
            var rawWidgetType = State.RawWidget.GetType().Name;
            if (rawWidgetType.EndsWith("State"))
                rawWidgetType = rawWidgetType.Substring(0, rawWidgetType.Length - "State".Length);

            this.name = $"{rawWidgetType}[MultiChildLayoutView]";
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
                        "Use SizedBox.Shrink() if necessary.");
                }

                var layoutData = childrenLayout[i];
                var size = layoutData.Size;

#if UNITY_EDITOR
                var infiniteWidth = float.IsInfinity(size.x);
                var infiniteHeight = float.IsInfinity(size.y);
                if (infiniteWidth || infiniteHeight)
                {
                    var axis = infiniteWidth && infiniteHeight ? "width and height"
                        : infiniteWidth ? "width" : "height";
                    Debug.LogError(
                        $"{State.RawWidget.GetType().Name}: child {child.GetType().Name} at position {i} " +
                        $"has an unbounded {axis}. Wrap it (e.g. Expanded, SizedBox) to give it a finite size.",
                        this);

                    var available = ((RectTransform) transform).rect.size;
                    size = new Vector2(
                        infiniteWidth ? Mathf.Max(available.x, 1f) : size.x,
                        infiniteHeight ? Mathf.Max(available.y, 1f) : size.y);
                }
#endif

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

#if UNITY_EDITOR
                // Level-triggered: drawn for exactly as long as the fault is happening. The report
                // that says it *started* was already made, once, by whoever set the marker.
                if (infiniteWidth || infiniteHeight || layoutData.Issue.HasValue)
                {
                    _warnings.Paint(rt);
                }
#endif
                // SYNC UNITY HIERARCHY WITH DECLARATIVE ORDER
                if (rt.GetSiblingIndex() != i)
                {
                    rt.SetSiblingIndex(i);
                }
            }

#if UNITY_EDITOR
            _warnings.HideUnused();
#endif
        }

#if UNITY_EDITOR
        private readonly LayoutWarningOverlay _warnings = new LayoutWarningOverlay();
#endif
    }
}