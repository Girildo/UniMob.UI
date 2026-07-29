using System;
using System.Collections.Generic;
using UniMob.UI.Internal;
using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Layout.Internal.Views;
using UnityEngine;
using UnityEngine.UI;

[assembly: RegisterComponentViewFactory("$$_Layout.MultiChildLayoutView",
    typeof(RectTransform),
    typeof(MultiChildLayoutView))]

namespace UniMob.UI.Layout.Internal.Views
{
    public interface IMultiChildLayoutState : ILayoutMetricsState
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
                if (infiniteWidth || infiniteHeight || layoutData.DebugWarning != null)
                {
                    if (layoutData.DebugWarning != null)
                        Debug.LogWarning($"{child.GetType().Name}: {layoutData.DebugWarning}", this);
                    PaintLayoutWarning(rt);
                }
#endif
                // SYNC UNITY HIERARCHY WITH DECLARATIVE ORDER
                if (rt.GetSiblingIndex() != i)
                {
                    rt.SetSiblingIndex(i);
                }
            }

#if UNITY_EDITOR
            HideUnusedLayoutWarnings();
#endif
        }

        // Diagnostics code

#if UNITY_EDITOR
        private static Sprite _stripeSprite;
        private readonly List<UnityEngine.UI.Image> _warningPool = new();
        private int _warningsUsed;

        private static Sprite StripeSprite
        {
            get
            {
                if (_stripeSprite != null) return _stripeSprite;

                const int size = 16;
                var tex = new Texture2D(size, size) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                        pixels[y * size + x] = (x + y) % size < size / 2
                            ? new Color32(255, 220, 0, 220)
                            : new Color32(20, 20, 20, 220);
                tex.SetPixels32(pixels);
                tex.Apply();

                _stripeSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.zero, size, 0, SpriteMeshType.FullRect);
                _stripeSprite.hideFlags = HideFlags.DontSave;
                return _stripeSprite;
            }
        }

        private void PaintLayoutWarning(RectTransform childRect)
        {
            UnityEngine.UI.Image overlay;
            if (_warningsUsed < _warningPool.Count)
            {
                overlay = _warningPool[_warningsUsed];
                overlay.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("LayoutWarning", typeof(RectTransform), typeof(UnityEngine.UI.Image))
                {
                    hideFlags = HideFlags.DontSave
                };
                overlay = go.GetComponent<UnityEngine.UI.Image>();
                overlay.sprite = StripeSprite;
                overlay.type = UnityEngine.UI.Image.Type.Tiled;
                overlay.raycastTarget = false;
                _warningPool.Add(overlay);
            }
            _warningsUsed++;

            var rt = (RectTransform) overlay.transform;
            rt.SetParent(childRect.parent, false); // sibling of the child, same coordinate space
            rt.SetAsLastSibling();                 // paint on top
            rt.anchorMin = childRect.anchorMin;
            rt.anchorMax = childRect.anchorMax;
            rt.pivot = childRect.pivot;
            rt.anchoredPosition = childRect.anchoredPosition;
            rt.sizeDelta = childRect.sizeDelta;
        }

        private void HideUnusedLayoutWarnings()
        {
            for (var i = _warningsUsed; i < _warningPool.Count; i++)
                _warningPool[i].gameObject.SetActive(false);
            _warningsUsed = 0;
        }
#endif

    }
}