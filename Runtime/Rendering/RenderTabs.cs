using System.Collections.Generic;
using UniMob.UI.Internal.Views;
using UnityEngine;

namespace UniMob.UI.Rendering
{
    public interface ITabsLayoutState : IMultiChildLayoutState
    {
        TabController TabController { get; }
    }

    /// <summary>
    ///     Lays every child out as one full-viewport page and slides the run of them horizontally as the
    ///     controller moves, so page <c>i</c> sits at <c>(i - Value) * width</c>.
    /// </summary>
    /// <remarks>
    ///     Pages are laid out with identical tight constraints rather than each to its own content. That is
    ///     what makes them pages: a tab must not resize the body when it is selected, and the slide is only
    ///     coherent if every page is the same width.
    /// </remarks>
    public class RenderTabs : MultiChildRenderObject
    {
        private readonly ITabsLayoutState _state;

        /// <summary>Pages are laid out side by side and scrolled through, so all but one sit outside.</summary>
        protected override bool ChildrenMayOverhang => true;

        public RenderTabs(ITabsLayoutState state)
            : base(state)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            ChildrenLayoutBuffer.Clear();

            var pageSize = ResolvePageSize(constraints);
            var pageConstraints = LayoutConstraints.Tight(pageSize.x, pageSize.y);

            foreach (var child in _state.Children)
            {
                LayoutChild(child, pageConstraints);
                ChildrenLayoutBuffer.Add(new LayoutInfo { Size = pageSize });
            }

            return pageSize;
        }

        /// <summary>
        ///     A page is the viewport. Where an axis is unbounded there is no viewport to take, so the pages
        ///     are measured loosely and the largest one sets the box for all of them -- they stay
        ///     interchangeable either way.
        /// </summary>
        private Vector2 ResolvePageSize(LayoutConstraints constraints)
        {
            if (constraints.HasBoundedWidth && constraints.HasBoundedHeight)
            {
                return constraints.Largest;
            }

            var looseConstraints = constraints.Loosen();
            var largest = Vector2.zero;

            foreach (var child in _state.Children)
            {
                largest = Vector2.Max(largest, LayoutChild(child, looseConstraints));
            }

            return constraints.Constrain(largest);
        }

        protected override void PerformPositioning(Vector2 size)
        {
            // Reading Value here is what animates the slide: it is an atom, so positioning re-runs for
            // each frame the controller tweens through, not just on the final index.
            var scrolled = _state.TabController.Value;

            for (var i = 0; i < ChildrenLayoutBuffer.Count; i++)
            {
                var layout = ChildrenLayoutBuffer[i];
                layout.Position = new Vector2((i - scrolled) * size.x, 0f);
                ChildrenLayoutBuffer[i] = layout;
            }
        }

        protected override float ComputeIntrinsicWidth(float height)
        {
            var widest = 0f;
            foreach (var child in _state.Children)
            {
                widest = Mathf.Max(widest, child.RenderObject.GetIntrinsicWidth(height));
            }

            return widest;
        }

        protected override float ComputeIntrinsicHeight(float width)
        {
            var tallest = 0f;
            foreach (var child in _state.Children)
            {
                tallest = Mathf.Max(tallest, child.RenderObject.GetIntrinsicHeight(width));
            }

            return tallest;
        }
    }
}
