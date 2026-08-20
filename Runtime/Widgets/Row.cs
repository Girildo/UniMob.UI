using System.Collections.Generic;
using UniMob.UI.Rendering;

namespace UniMob.UI.Widgets
{
    public class Row : StatefulWidget, IMultiChildLayoutWidget
    {
        public List<Widget> Children { get; init; } = new List<Widget>();
        public CrossAxisAlignment CrossAxisAlignment { get; init; }
        public MainAxisAlignment MainAxisAlignment { get; init; }
        public AxisSize MainAxisSize { get; init; } = AxisSize.Min;

        public float Spacing { get; init; }

        public override State CreateState() => new RowState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderFlex((RowState)state, Axis.Horizontal);
        }
    }

    internal class RowState : ViewState<Row>, IFlexContainerState
    {
        public IState[] Children => _children.Value;

        private readonly StateCollectionHolder _children;

        public RowState()
        {
            _children = CreateChildren(context => Widget.Children);
        }

        public override WidgetViewReference View =>
            WidgetViewReference.Registered("UniMob.MultiChildLayoutView");

        public CrossAxisAlignment CrossAxisAlignment => Widget.CrossAxisAlignment;

        public MainAxisAlignment MainAxisAlignment => Widget.MainAxisAlignment;

        public AxisSize MainAxisSize => Widget.MainAxisSize;

        public float Spacing => Widget.Spacing;
    }
}
