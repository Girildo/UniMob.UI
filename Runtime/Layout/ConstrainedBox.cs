using System;
using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI.Layout
{
    public class ConstrainedBox : SingleChildLayoutWidget
    {
        public LayoutConstraints BoxConstraints { get; set; }

        public override State CreateState()
        {
            return new ConstrainedBoxState();
        }

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderConstrainedBox((ConstrainedBoxState) state);
        }
    }


    internal class ConstrainedBoxState : SingleChildLayoutState<ConstrainedBox>, IConstrainedBoxState
    {
        public LayoutConstraints BoxConstraints => Widget.BoxConstraints;
    }
}