using System.Collections.Generic;
using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI.Layout
{
    public class Row : StatefulWidget, IMultiChildLayoutWidget
    {
        public List<Widget> Children { get; set; } = new List<Widget>();
        public CrossAxisAlignment CrossAxisAlignment { get; set; }
        public MainAxisAlignment MainAxisAlignment { get; set; }
        public AxisSize MainAxisSize { get; set; } = AxisSize.Min;

        public float Spacing { get; set; }

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
            WidgetViewReference.Resource("$$_Layout.MultiChildLayoutView");

        public CrossAxisAlignment CrossAxisAlignment => Widget.CrossAxisAlignment;

        public MainAxisAlignment MainAxisAlignment => Widget.MainAxisAlignment;

        public AxisSize MainAxisSize => Widget.MainAxisSize;

        public float Spacing => Widget.Spacing;
    }
}
