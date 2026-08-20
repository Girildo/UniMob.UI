using System.Collections.Generic;
using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Layout.Internal.Views;

namespace UniMob.UI.Layout
{
    public class ZStack : StatefulWidget, IMultiChildLayoutWidget
    {
        public List<Widget> Children { get; init; } = new List<Widget>();
        public Alignment Alignment { get; init; } = Alignment.Center;

        public override State CreateState() => new ZStackState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderZStack((ZStackState)state);
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
        public override WidgetViewReference View =>
            WidgetViewReference.Resource("$$_Layout.MultiChildLayoutView");
    }
}
