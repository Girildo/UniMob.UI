#nullable enable
namespace UniMob.UI.Layout
{
    public interface ISingleChildLayoutState : ILayoutMetricsState
    {
        IState? Child { get; }
    }
}