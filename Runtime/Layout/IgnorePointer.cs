using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Layout.Internal.Views;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// Makes its subtree invisible to hit testing while <see cref="Ignoring"/> is set: pointer events
    /// pass through it to whatever is behind.
    /// </summary>
    public class IgnorePointer : SingleChildLayoutWidget
    {
        public bool Ignoring { get; set; }
        public override State CreateState() => new IgnorePointerState();
        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderProxy((IgnorePointerState)state);
        }
    }
    internal class IgnorePointerState : SingleChildLayoutState<IgnorePointer>, IIgnorePointerState
    {
        public override WidgetViewReference View { get; }
            = WidgetViewReference.Resource("$$_Layout.IgnorePointer");
        public bool Ignoring => Widget.Ignoring;
        
    }
}