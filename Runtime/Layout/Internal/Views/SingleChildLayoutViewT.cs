using System;
using UniMob.UI.Diagnostics;
using UniMob.UI.Internal;
using UniMob.UI.Layout.Internal.Diagnostics;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;


namespace UniMob.UI.Layout.Internal.Views
{
    /// <summary>
    /// Represents a base class for views that manage a single child layout, providing functionality to render and
    /// position a single child element within a layout container.
    /// </summary>
    /// <remarks>This class is designed to work with a single child layout model, where the layout state
    /// defines the size and position of the child element.  It requires the associated GameObject to have a <see
    /// cref="RectTransform"/>  and a <see cref="CanvasRenderer"/> component.  The layout logic ensures that the child
    /// element is positioned and sized  according to the layout state provided by the <typeparamref
    /// name="TState"/>.</remarks>
    /// <typeparam name="TState">The type of the state object associated with the view </typeparam> 
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public abstract class SingleChildLayoutView<TState> : View<TState>
        where TState : class, ISingleChildLayoutState
    {
        private ViewMapperBase _mapper;

        protected IView ChildView { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            _mapper = new PooledViewMapper(transform);
        }


        protected override void Render()
        {

            if (State.RenderObject is not ISingleChildRenderObject renderObject)
            {
                // Structural, so it stays a throw: a view paired with the wrong render object has no
                // layout to continue with. The path is what makes it findable.
                throw new System.InvalidOperationException(
                    $"{typeof(SingleChildLayoutView).Name} expects the state to return a "
                        + $"{nameof(ISingleChildRenderObject)}. {State.GetType().Name} returned a "
                        + $"{State.RenderObject.GetType().Name} instead.\n"
                        + $"  at      {WidgetPath.From(State)}"
                );
            }

            if (State.Child == null)
                return;

#if UNITY_EDITOR

            var rawWidgetType = State.RawWidget.GetType().Name;
            if (rawWidgetType.EndsWith("State"))
                rawWidgetType = rawWidgetType.Substring(0, rawWidgetType.Length - "State".Length);

            this.name = $"{rawWidgetType}[SingleChildLayoutView<{typeof(TState).Name}>]";

#endif

            using (var render = _mapper.CreateRender())
            {
                var child = State.Child;
                this.ChildView = render.RenderItem(child);

                var rt = this.ChildView.rectTransform;
                rt.anchorMin = Vector2.up;
                rt.anchorMax = Vector2.up;

                var childSize = renderObject.ChildSize;
                var topLeftPosition = renderObject.ChildPosition;

                var pivotOffset = new Vector2(
                    childSize.x * rt.pivot.x,
                    -childSize.y * (1.0f - rt.pivot.y)
                );

                rt.sizeDelta = childSize;
                rt.anchoredPosition = new Vector2(topLeftPosition.x, -topLeftPosition.y) + pivotOffset;

#if UNITY_EDITOR
                // The single-child half of the tree could report a fault and show nothing for it,
                // because the stripe only ever existed on the multi-child view. There is no per-child
                // marker to read here -- a single-child render object keeps one child's geometry, not
                // a buffer of it -- so the signal is the render object's own live issue flag.
                if (State.RenderObject.HasLayoutIssue)
                {
                    _warnings.Paint(rt);
                }

                _warnings.HideUnused();
#endif
            }
        }

#if UNITY_EDITOR
        private readonly LayoutWarningOverlay _warnings = new LayoutWarningOverlay();

        private void OnDrawGizmosSelected()
        {
            if (this.rectTransform == null) return;

            if (UnityEditor.Selection.activeGameObject != this.gameObject) return;

            Matrix4x4 originalMatrix = Gizmos.matrix;
            Gizmos.matrix = this.rectTransform.localToWorldMatrix;

            // --- PARENT VIEW (BLUE) ---
            Color parentBorder = new Color(0.2f, 0.6f, 1.0f, 0.9f);
            Color parentFill = new Color(0.2f, 0.6f, 1.0f, 0.1f);

            Gizmos.color = parentFill;
            Gizmos.DrawCube(this.rectTransform.rect.center, this.rectTransform.rect.size);
            Gizmos.color = parentBorder;
            Gizmos.DrawWireCube(this.rectTransform.rect.center, this.rectTransform.rect.size);

            string parentText = $"{this.gameObject.name} [{this.rectTransform.rect.width}x{this.rectTransform.rect.height}]";

            // FIX 2: Place Parent Label at the TOP-LEFT
            Vector3 parentLabelPos = new Vector3(this.rectTransform.rect.xMin, this.rectTransform.rect.yMax + 2f, 0f);
            DrawOutlinedLabel(parentLabelPos, parentText, parentBorder);


            // --- CHILD VIEW (GREEN) ---
            if (this.State != null && this.State.RenderObject is ISingleChildRenderObject renderObj)
            {
                Color childBorder = new Color(0.2f, 0.8f, 0.2f, 0.9f);
                Color childFill = new Color(0.2f, 0.8f, 0.2f, 0.15f);

                var childSize = renderObj.ChildSize;
                var topLeftPosition = renderObj.ChildPosition;

                float childLocalXMin = this.rectTransform.rect.xMin + topLeftPosition.x;
                float childLocalYMax = this.rectTransform.rect.yMax - topLeftPosition.y;

                Vector3 childCenterLocal = new Vector3(
                    childLocalXMin + (childSize.x / 2f),
                    childLocalYMax - (childSize.y / 2f),
                    0f
                );
                Vector3 childSize3D = new Vector3(childSize.x, childSize.y, 0f);

                Gizmos.color = childFill;
                Gizmos.DrawCube(childCenterLocal, childSize3D);
                Gizmos.color = childBorder;
                Gizmos.DrawWireCube(childCenterLocal, childSize3D);

                string childText = $"Child [{childSize.x}x{childSize.y}]";

                // FIX 3: Place Child Label at the BOTTOM-LEFT. 
                // Even if the child is the exact same size as the parent, the text will never overlap.
                Vector3 childLabelPos = new Vector3(childLocalXMin, childLocalYMax - childSize.y - 15f, 0f);
                DrawOutlinedLabel(childLabelPos, childText, childBorder);
            }

            Gizmos.matrix = originalMatrix;
        }

        // Helper method to draw text that is actually readable in the Scene view
        private void DrawOutlinedLabel(Vector3 position, string text, Color textColor)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerLeft
            };

            // Draw a fake black outline by offsetting the text slightly
            style.normal.textColor = Color.black;
            UnityEditor.Handles.Label(position + new Vector3(1, -1, 0), text, style);
            UnityEditor.Handles.Label(position + new Vector3(-1, 1, 0), text, style);
            UnityEditor.Handles.Label(position + new Vector3(1, 1, 0), text, style);
            UnityEditor.Handles.Label(position + new Vector3(-1, -1, 0), text, style);

            // Draw the actual colored text on top
            style.normal.textColor = textColor;
            UnityEditor.Handles.Label(position, text, style);
        }
#endif

    }
}