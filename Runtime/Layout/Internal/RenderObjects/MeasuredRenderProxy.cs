using System;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public class MeasuredRenderProxy : RenderProxy
    {
        private readonly Action<Vector2> onSize;
        private Vector2 lastReported = new(float.NaN, float.NaN);

        public MeasuredRenderProxy(ISingleChildLayoutState state, Action<Vector2> onSize)
            : base(state) => this.onSize = onSize;

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            var size = base.PerformSizing(constraints); // child's resolved size, logical space
            if (size != this.lastReported)              // only fire on actual change
            {
                this.lastReported = size;
                this.onSize(size);
            }
            return size;
        }
    }
}