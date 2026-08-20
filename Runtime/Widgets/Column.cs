using System.Collections.Generic;
using UniMob.UI.Rendering;

namespace UniMob.UI.Widgets
{
    public class Column : StatefulWidget, IMultiChildLayoutWidget
    {
        public List<Widget> Children { get; init; } = new();
        public CrossAxisAlignment CrossAxisAlignment { get; init; }
        public MainAxisAlignment MainAxisAlignment { get; init; }

        public AxisSize MainAxisSize { get; init; } = AxisSize.Min;

        public float Spacing { get; init; } = 0f;

        public override State CreateState() => new ColumnState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderFlex((ColumnState)state, Axis.Vertical);
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

        public override WidgetViewReference View =>
            WidgetViewReference.Registered("UniMob.MultiChildLayoutView");

        public CrossAxisAlignment CrossAxisAlignment => Widget.CrossAxisAlignment;

        public MainAxisAlignment MainAxisAlignment => Widget.MainAxisAlignment;

        public AxisSize MainAxisSize => Widget.MainAxisSize;

        public float Spacing => Widget.Spacing;
    }
}
