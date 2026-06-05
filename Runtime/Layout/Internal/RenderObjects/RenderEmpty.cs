using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public class RenderEmpty : LeafRenderObject
    {
        public static readonly RenderEmpty Shared = new RenderEmpty();

        public RenderEmpty() : base(Lifetime.Eternal)
        {

        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            return Vector2.zero;
        }

        protected override float ComputeIntrinsicWidth(float height)
        {
            return 0f;
        }

        protected override float ComputeIntrinsicHeight(float width)
        {
            return 0f;
        }
    }
}