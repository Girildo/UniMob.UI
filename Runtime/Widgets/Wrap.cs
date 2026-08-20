using System.Collections.Generic;
using UniMob.UI.Internal.Views;
using UniMob.UI.Rendering;

namespace UniMob.UI.Widgets
{
    public enum WrapAlignment
    {
        Start,
        End,
        Center,
    }

    public class Wrap : StatefulWidget, IMultiChildLayoutWidget
    {
        public List<Widget> Children { get; init; } = new List<Widget>();

        /// <summary>
        /// Direction of a run.
        /// If horizontal, children will be placed in a row and wrap to the next line when they exceed the available width.
        /// If vertical, children will be placed in a column and wrap to the next column when they exceed the available height.
        /// </summary>
        public Axis Direction { get; init; } = Axis.Horizontal;

        /// <summary>
        /// The spacing between children in the main axis.
        /// </summary>
        public float Spacing { get; init; } = 8f;

        /// <summary>
        /// The spacing between runs in the cross axis.
        /// </summary>
        public float RunSpacing { get; init; } = 8f;

        /// <summary>
        /// How the children within a run should be placed in the main axis.
        /// </summary>
        public MainAxisAlignment Alignment { get; init; } = MainAxisAlignment.Start;

        /// <summary>
        /// How the children within a run should be aligned relative to each other in the cross axis.
        /// </summary>
        public CrossAxisAlignment CrossAxisAlignment { get; init; } = CrossAxisAlignment.Start;

        /// <summary>
        ///  How the runs themselves should be placed in the cross axis.
        /// </summary>
        public MainAxisAlignment RunAlignment { get; init; } = MainAxisAlignment.Start;

        public override State CreateState() => new WrapState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderWrap((WrapState)state);
        }
    }

    internal class WrapState : ViewState<Wrap>, IWrapState
    {
        public IState[] Children => _children.Value;
        private readonly StateCollectionHolder _children;

        public WrapState()
        {
            _children = CreateChildren(context => Widget.Children);
        }

        public override WidgetViewReference View =>
            WidgetViewReference.Registered("UniMob.MultiChildLayoutView");

        public Axis Direction => this.Widget.Direction;
        public float Spacing => this.Widget.Spacing;
        public float RunSpacing => this.Widget.RunSpacing;

        public MainAxisAlignment Alignment => this.Widget.Alignment;
        public CrossAxisAlignment CrossAxisAlignment => this.Widget.CrossAxisAlignment;
        public MainAxisAlignment RunAlignment => this.Widget.RunAlignment;
    }
}
