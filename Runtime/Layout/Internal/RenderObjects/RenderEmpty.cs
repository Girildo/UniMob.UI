using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public class RenderEmpty : LeafRenderObject
    {
        // Deliberately not a shared singleton. A render object owns its constraints and its memoized
        // pass, so one instance behind every Empty in the app would mean each parent's layout write
        // invalidating every other parent's. Sizing to zero unconditionally makes sharing look free;
        // owning layout state is what makes it not.
        public RenderEmpty(IState state)
            : base(state) { }

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
