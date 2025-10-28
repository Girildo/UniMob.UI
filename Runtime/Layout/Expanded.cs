using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Widgets;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// A widget that expands a child of a Row or Column to fill the available space.
    /// This is a signal widget and does not create its own RenderObject.
    /// </summary>
    public class Expanded : SingleChildLayoutWidget
    {
        public int Flex { get; set; } = 1;
        
        public override State CreateState() => new ExpandedState();
        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderProxy((ExpandedState)state);
        }
    }

    public class ExpandedState : SingleChildLayoutState<Expanded>
    {
        public int Flex => Widget.Flex;
    }
}