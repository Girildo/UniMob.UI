using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Minimal leaf widget that reports a fixed, caller-specified size regardless of incoming
    ///     constraints (clamped to whatever the parent enforces). Used as inert filler content in layout
    ///     tests so a latent bug in a *real* leaf widget (e.g. <see cref="SizedBox"/>) can't mask or fake
    ///     a result in the container actually under test.
    /// </summary>
    public class FixedSizeBox : Widget
    {
        public Vector2 Size { get; set; }

        public System.Type Type => typeof(FixedSizeBox);
        public Key Key { get; set; }

        public State CreateState(StateProvider provider) => null;

        public State CreateState() => new FixedSizeBoxState();

        public RenderObject CreateRenderObject(BuildContext context, IState state) =>
            new RenderFixedSizeBox((FixedSizeBoxState)state);

        public string GetDiagnosticInfo() => null;
    }

    public class FixedSizeBoxState : ViewState<FixedSizeBox>
    {
        public Vector2 BoxSize => Widget.Size;

        // Never actually dereferenced in tests: FixedSizeBox is only ever mounted headlessly via
        // TestHarness.Mount, never attached to a real View/Canvas. Any WidgetViewReference works
        // here -- this one just happens to point at a resource that does exist, in case a future
        // change makes it load-bearing.
        public override WidgetViewReference View =>
            WidgetViewReference.Resource("Layout/UniMob.Text");
    }

    public class RenderFixedSizeBox : LeafRenderObject
    {
        private readonly FixedSizeBoxState _state;

        public RenderFixedSizeBox(FixedSizeBoxState state)
            : base(state)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            return constraints.Constrain(_state.BoxSize);
        }

        protected override float ComputeIntrinsicWidth(float height) => _state.BoxSize.x;

        protected override float ComputeIntrinsicHeight(float width) => _state.BoxSize.y;
    }
}
