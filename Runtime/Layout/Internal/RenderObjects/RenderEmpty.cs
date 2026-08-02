using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public class RenderEmpty : LeafRenderObject
    {
        // Deliberately not a shared singleton. A render object owns its constraints and its memoized
        // pass, so one instance behind every Empty in the app would mean each parent's layout write
        // invalidating every other parent's. Being stateless was what made sharing look free.
        public RenderEmpty(IState state)
            : base(state.StateLifetime) { }

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