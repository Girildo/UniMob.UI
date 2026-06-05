using System.Collections.Generic;
using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI.Layout
{
    public class Column : StatefulWidget, IMultiChildLayoutWidget
    {
        public List<Widget> Children { get; set; } = new();
        public CrossAxisAlignment CrossAxisAlignment { get; set; }
        public MainAxisAlignment MainAxisAlignment { get; set; }

        public AxisSize MainAxisSize { get; set; } = AxisSize.Min;

        public float Spacing { get; set; } = 0f;


        public override State CreateState() => new ColumnState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderFlex((ColumnState) state, Axis.Vertical);
        }
    }

    internal class ColumnState : ViewState<Column>, IFlexContainerState
    {
        private readonly StateCollectionHolder _children;

        public ColumnState()
        {
            _children = CreateChildren(context => Widget.Children);
        }

        public IState[] Children => _children.Value;

        public override WidgetViewReference View => WidgetViewReference.Resource("$$_Layout.MultiChildLayoutView");

        public CrossAxisAlignment CrossAxisAlignment => Widget.CrossAxisAlignment;

        public MainAxisAlignment MainAxisAlignment => Widget.MainAxisAlignment;

        public AxisSize MainAxisSize => Widget.MainAxisSize;

        public float Spacing => Widget.Spacing;
    }
}