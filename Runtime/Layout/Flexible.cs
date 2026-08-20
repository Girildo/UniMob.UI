using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI.Layout
{
    public enum FlexFit
    {
        Loose,
        Tight,
    }

    internal interface IFlexible : ISingleChildLayoutWidget
    {
        int Flex { get; }
        FlexFit Fit { get; }
    }

    public class Expanded : SingleChildLayoutWidget, IFlexible
    {
        public int Flex { get; init; } = 1;

        public FlexFit Fit => FlexFit.Tight;

        public override State CreateState() => new FlexibleState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderProxy((FlexibleState)state);
        }
    }

    public class Flexible : SingleChildLayoutWidget, IFlexible
    {
        public int Flex { get; init; } = 1;
        public FlexFit Fit { get; init; } = FlexFit.Loose;

        public override State CreateState() => new FlexibleState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderProxy((FlexibleState)state);
        }
    }

    internal class FlexibleState : SingleChildLayoutState<IFlexible>
    {
        public int Flex => Widget.Flex;
        public FlexFit Fit => Widget.Fit;
    }
}
