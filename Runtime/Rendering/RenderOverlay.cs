using UnityEngine;

namespace UniMob.UI.Rendering
{
    /// <summary>
    ///     Lays out an overlay's entries as a full-bleed stack: the layer fills the region its parent
    ///     gives it, and every entry is sized to that full area and aligned to the top-left, so a later
    ///     entry paints over an earlier one.
    /// </summary>
    /// <remarks>
    ///     Tight and full-bleed rather than loose, which is what lets an entry place itself with
    ///     <c>AnchoredBox</c>: that widget measures as zero under a parent that stretches instead of
    ///     constraining. An entry that wants to be small says so inside its own box.
    /// </remarks>
    public class RenderOverlay : MultiChildRenderObject
    {
        private readonly IOverlayState _state;

        public RenderOverlay(IOverlayState state)
            : base(state)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            ChildrenLayoutBuffer.Clear();

            var entries = _state.Children;

            // The layer fills the region it's given.
            var width = constraints.HasBoundedWidth ? constraints.MaxWidth : 0f;
            var height = constraints.HasBoundedHeight ? constraints.MaxHeight : 0f;

            // Unbounded fallback (rare): grow to the largest entry on the unbounded axis so we never
            // hand an infinite size to the view layer.
            if (!constraints.HasBoundedWidth || !constraints.HasBoundedHeight)
            {
                var loose = constraints.Loosen();
                foreach (var entry in entries)
                {
                    var measured = LayoutChild(entry, loose);
                    if (!constraints.HasBoundedWidth)
                        width = Mathf.Max(width, measured.x);
                    if (!constraints.HasBoundedHeight)
                        height = Mathf.Max(height, measured.y);
                }
            }

            var size = constraints.Constrain(new Vector2(width, height));

            var entryConstraints = LayoutConstraints.Tight(size.x, size.y);
            foreach (var entry in entries)
            {
                var entrySize = LayoutChild(entry, entryConstraints);
                ChildrenLayoutBuffer.Add(
                    new LayoutInfo { Size = entrySize, Position = Vector2.zero }
                );
            }

            return size;
        }

        protected override void PerformPositioning(Vector2 size)
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
