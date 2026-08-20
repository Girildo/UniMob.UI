#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNIMOB_UI_FORCE_DIAGNOSTICS
#define UNIMOB_UI_DIAGNOSTICS
#endif

using System;
using System.Diagnostics;
using UniMob.UI.Diagnostics;
using UniMob.UI.Internal;
using UniMob.UI.Rendering;
using UnityEngine;
#if UNIMOB_UI_DIAGNOSTICS
// LayoutWarningOverlay is the only type in this namespace and it compiles out with the diagnostics,
// taking the namespace with it -- so an unguarded using of it is a compile error in a build without.
#endif

namespace UniMob.UI.Internal.Views
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
            {
                // A child that has gone away still has a view to recycle, and an empty mapper pass
                // is what returns it to the pool. Skipping the pass leaves that view mounted, still
                // bound to its old state, and painting its last frame forever.
                using (_mapper.CreateRender()) { }
                this.ChildView = null;

                // No child to place, but this render object can still be the one at fault -- one that
                // answers with a non-finite size of its own has nothing below it to blame. Returning
                // here is what made that invisible.
                PaintOwnIssue();
                return;
            }

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
                rt.anchoredPosition =
                    new Vector2(topLeftPosition.x, -topLeftPosition.y) + pivotOffset;
            }

            PaintOwnIssue();
        }

        /// <summary>
        ///     Stripes this widget's own box while its render object has a live fault.
        /// </summary>
        /// <remarks>
        ///     Its own box, not its child's: a single-child render object keeps one child's geometry
        ///     rather than a buffer of per-child markers, so there is nothing here that says which side
        ///     of the pair is at fault -- and for the faults that reach this half of the tree, an
        ///     unbounded frame or a non-finite size of its own, the answer is this widget.
        ///     <para>
        ///         Called on every path, including the one that returns early with no child, so a fault
        ///         that clears always un-draws.
        ///     </para>
        /// </remarks>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNIMOB_UI_FORCE_DIAGNOSTICS")]
        private void PaintOwnIssue()
        {
#if UNIMOB_UI_DIAGNOSTICS
            if (State.RenderObject.HasLayoutIssue && this.rectTransform.parent != null)
            {
                _warnings.Paint(this.rectTransform);
            }

            _warnings.HideUnused();
#endif
        }

#if UNIMOB_UI_DIAGNOSTICS
        private readonly LayoutWarningOverlay _warnings = new LayoutWarningOverlay();
#endif
    }
}
