using UniMob.UI.Rendering;

namespace UniMob.UI.Widgets
{
    public class Empty : StatefulWidget
    {
        public override State CreateState() => new EmptyState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderEmpty(state);
        }
    }

    internal class EmptyState : ViewState<Empty>, IEmptyState
    {
        public override WidgetViewReference View { get; } =
            WidgetViewReference.Registered("UniMob.EmptyView");
    }
}
