using System;
using UniMob.UI.Rendering;

namespace UniMob.UI.Widgets
{
    public class ConstrainedBox : SingleChildLayoutWidget
    {
        public LayoutConstraints BoxConstraints { get; init; }

        public override State CreateState()
        {
            return new ConstrainedBoxState();
        }

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderConstrainedBox((ConstrainedBoxState)state);
        }
    }

    internal class ConstrainedBoxState
        : SingleChildLayoutState<ConstrainedBox>,
            IConstrainedBoxState
    {
        public LayoutConstraints BoxConstraints => Widget.BoxConstraints;
    }
}
