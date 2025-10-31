using JetBrains.Annotations;
using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// A widget that controls where a child of a ZStack is positioned.
    /// This is a signal widget and does not create its own RenderObject.
    /// </summary>
    public class Positioned : SingleChildLayoutWidget
    {
        public float? Left { get; set; }
        public float? Top { get; set; }
        public float? Right { get; set; }
        public float? Bottom { get; set; }
        public float? Width { get; set; }
        public float? Height { get; set; }

        public override State CreateState() => new PositionedState();
        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderProxy((PositionedState)state);
        }
    }

    public class PositionedState : SingleChildLayoutState<Positioned>
    {
        
    }
}