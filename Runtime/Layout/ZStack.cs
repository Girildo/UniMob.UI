using System.Collections.Generic;
using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Layout.Internal.Views;
using UniMob.UI.Widgets;

namespace UniMob.UI.Layout
{
    public class ZStack : StatefulWidget, IMultiChildLayoutWidget 
    {
        public List<Widget> Children { get; set; } = new List<Widget>();
        public Alignment Alignment { get; set; } = Alignment.Center;

        // Not used by ZStack, but part of the interface
        public MainAxisAlignment MainAxisAlignment => MainAxisAlignment.Start;
        public CrossAxisAlignment CrossAxisAlignment => CrossAxisAlignment.Start;
        public AxisSize MainAxisSize => AxisSize.Min;

        public override State CreateState() => new ZStackState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderZStack((ZStackState) state);
        }
    }

    public interface IZStackState : IMultiChildLayoutState
    {
        Alignment Alignment { get; }
    }

    public class ZStackState : ViewState<ZStack>, IZStackState
    {
        private readonly StateCollectionHolder _children;
        public Alignment Alignment => this.Widget.Alignment;

        public ZStackState()
        {
            _children = CreateChildren(context => Widget.Children);
        }

        public IState[] Children => _children.Value;
        public override WidgetViewReference View => WidgetViewReference.Resource("$$_Layout.MultiChildLayoutView");

    }
}