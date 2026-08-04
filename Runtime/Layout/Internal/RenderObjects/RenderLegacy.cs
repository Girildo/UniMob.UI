using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public class RenderLegacy : LeafRenderObject
    {
        private readonly IViewState _state;

        public RenderLegacy(IViewState state) : base(state)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            if(_state.StateLifetime.IsDisposed)
                return Vector2.zero;
            var legacySize = _state.Size;
            var desired = legacySize.GetSizeUnbounded();

            // An infinite axis here is the bridge's way of saying "I expand", and is skipped. A finite
            // one is a legacy widget stating a concrete size that does not fit the slot it was put in.
            ReportContentOverflow(constraints, desired, GiveTheLegacyWidgetItsSize);

            return constraints.Constrain(desired);
        }

        private const string GiveTheLegacyWidgetItsSize =
            "This legacy widget states a size larger than the slot it was given. Enlarge the slot, or "
            + "let the widget expand instead of naming a size.";
        

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