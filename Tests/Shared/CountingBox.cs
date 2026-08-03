using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Leaf widget that records every sizing pass it is asked to perform, and the constraints it
    ///     was handed each time.
    /// </summary>
    /// <remarks>
    ///     Deliberately not <see cref="FixedSizeBox"/>: tests built on this one assert on the
    ///     <em>number</em> of layout passes and on what each pass was told, which is the only way to
    ///     observe a render object being driven twice, or being driven with the wrong constraints.
    ///     The size it reports is otherwise the same inert, constraint-clamped fixed size.
    /// </remarks>
    public class CountingBox : Widget
    {
        public Vector2 BoxSize { get; set; }

        public System.Type Type => typeof(CountingBox);
        public Key Key { get; set; }

        public State CreateState(StateProvider provider) => null;

        public State CreateState() => new CountingBoxState();

        public RenderObject CreateRenderObject(BuildContext context, IState state) =>
            new RenderCountingBox((CountingBoxState)state);

        public string GetDiagnosticInfo() => null;
    }

    public class CountingBoxState : ViewState<CountingBox>
    {
        public Vector2 BoxSize => Widget.BoxSize;

        // Never dereferenced: CountingBox is only ever mounted headlessly via TestHarness.Mount, so
        // the prefab is never loaded. Mirrors FixedSizeBox, which explains this in full.
        public override WidgetViewReference View =>
            WidgetViewReference.Resource("Layout/UniMob.Text");
    }

    public class RenderCountingBox : LeafRenderObject
    {
        private readonly CountingBoxState _state;

        /// <summary>Number of times <c>PerformSizing</c> has run since this box was created.</summary>
        public int SizingPasses { get; private set; }

        /// <summary>Constraints handed to the most recent sizing pass.</summary>
        public LayoutConstraints LastConstraints { get; private set; }

        public RenderCountingBox(CountingBoxState state)
            : base(state)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            SizingPasses++;
            LastConstraints = constraints;
            return constraints.Constrain(_state.BoxSize);
        }

        protected override float ComputeIntrinsicWidth(float height) => _state.BoxSize.x;

        protected override float ComputeIntrinsicHeight(float width) => _state.BoxSize.y;
    }
}
