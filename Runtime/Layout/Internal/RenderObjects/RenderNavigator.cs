using System.Collections.Generic;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    /// <summary>
    ///     Lays out a <see cref="Navigator"/>'s screens as a full-bleed stack: the navigator fills the
    ///     region its parent gives it, and every screen is sized to that full area and aligned to the
    ///     top-left, so the topmost route covers the ones beneath it.
    /// </summary>
    public class RenderNavigator : RenderObject, IMultiChildrenRenderObject
    {
        private readonly INavigatorState _state;

        private readonly List<LayoutInfo> _childrenLayout = new();
        public IReadOnlyList<LayoutInfo> ChildrenLayout
        {
            get
            {
                // Pulls layout before handing the list out. The list is only valid immediately after a
                // pass, and nothing used to enforce that: a view that forgot the pull silently stamped
                // stale positions onto RectTransforms.
                WatchLayout();
                return _childrenLayout;
            }
        }

        public RenderNavigator(INavigatorState state) : base(state.StateLifetime)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            _childrenLayout.Clear();

            var screens = _state.Screens;

            // The navigator fills the region it's given.
            var width = constraints.HasBoundedWidth ? constraints.MaxWidth : 0f;
            var height = constraints.HasBoundedHeight ? constraints.MaxHeight : 0f;

            // Unbounded fallback (rare): grow to the largest screen on the unbounded axis so we
            // never hand an infinite size to the view layer.
            if (!constraints.HasBoundedWidth || !constraints.HasBoundedHeight)
            {
                var loose = constraints.Loosen();
                foreach (var screen in screens)
                {
                    var measured = LayoutChild(screen, loose);
                    if (!constraints.HasBoundedWidth)
                        width = Mathf.Max(width, measured.x);
                    if (!constraints.HasBoundedHeight)
                        height = Mathf.Max(height, measured.y);
                }
            }

            var size = constraints.Constrain(new Vector2(width, height));

            // Every screen is laid out to fill the navigator, stacked at the top-left corner.
            var screenConstraints = LayoutConstraints.Tight(size.x, size.y);
            foreach (var screen in screens)
            {
                var screenSize = LayoutChild(screen, screenConstraints);
                _childrenLayout.Add(new LayoutInfo { Size = screenSize, Position = Vector2.zero });
            }

            return size;
        }

        protected override void PerformPositioning()
        {
            for (var i = 0; i < _childrenLayout.Count; i++)
            {
                var info = _childrenLayout[i];
                info.Position = Vector2.zero;
                _childrenLayout[i] = info;
            }
        }

        protected override float ComputeIntrinsicWidth(float height) => float.PositiveInfinity;

        protected override float ComputeIntrinsicHeight(float width) => float.PositiveInfinity;
    }
}
