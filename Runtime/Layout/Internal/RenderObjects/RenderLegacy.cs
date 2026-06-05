using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public class RenderLegacy : LeafRenderObject
    {
        private readonly IViewState _state;

        public RenderLegacy(IViewState state) : base(state.StateLifetime)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            if(_state.StateLifetime.IsDisposed)
                return Vector2.zero;
            var legacySize = _state.Size;
            return constraints.Constrain(legacySize.GetSizeUnbounded());
        }
        

        protected override float ComputeIntrinsicWidth(float height)
        {
            return _state.Size.MaxWidth;
        }

        protected override float ComputeIntrinsicHeight(float width)
        {
            return _state.Size.MaxHeight;
        }
    }
}