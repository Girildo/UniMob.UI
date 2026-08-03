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
    public class RenderNavigator : MultiChildRenderObject
    {
        private readonly INavigatorState _state;


        public RenderNavigator(INavigatorState state) : base(state)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            ChildrenLayoutBuffer.Clear();

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
                ChildrenLayoutBuffer.Add(new LayoutInfo { Size = screenSize, Position = Vector2.zero });
            }

            return size;
        }

        protected override void PerformPositioning()
        {
            for (var i = 0; i < ChildrenLayoutBuffer.Count; i++)
            {
                var info = ChildrenLayoutBuffer[i];
                info.Position = Vector2.zero;
                ChildrenLayoutBuffer[i] = info;
            }
        }

        protected override float ComputeIntrinsicWidth(float height) => float.PositiveInfinity;

        protected override float ComputeIntrinsicHeight(float width) => float.PositiveInfinity;
    }
}
