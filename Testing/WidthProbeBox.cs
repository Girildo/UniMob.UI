using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     A leaf widget whose intrinsic height/width simply echoes back the width/height it was asked to
    ///     measure at. Used to verify exactly what cross-axis value a parent (e.g. <see cref="RenderFlex"/>'s
    ///     flex-aware intrinsic sizing) actually handed to a child, which <see cref="FixedSizeBox"/> can't
    ///     reveal since its intrinsic size never depends on the incoming argument.
    /// </summary>
    public class WidthProbeBox : Widget
    {
        public System.Type Type => typeof(WidthProbeBox);
        public Key Key { get; set; }

        public State CreateState(StateProvider provider) => null;

        public State CreateState() => new WidthProbeBoxState();

        public RenderObject CreateRenderObject(BuildContext context, IState state) =>
            new RenderWidthProbeBox((WidthProbeBoxState)state);

        public string GetDiagnosticInfo() => null;
    }

    public class WidthProbeBoxState : ViewState<WidthProbeBox>
    {
        // Never dereferenced -- see FixedSizeBoxState.View for why any reference is safe here.
        public override WidgetViewReference View =>
            WidgetViewReference.Resource("Layout/UniMob.Text");
    }

    public class RenderWidthProbeBox : LeafRenderObject
    {
        public RenderWidthProbeBox(WidthProbeBoxState state)
            : base(state) { }

        protected override Vector2 PerformSizing(LayoutConstraints constraints) =>
            new Vector2(constraints.MinWidth, constraints.MinHeight);

        protected override float ComputeIntrinsicWidth(float height) => height;

        protected override float ComputeIntrinsicHeight(float width) => width;
    }
}
