namespace UniMob.UI
{
    public interface IFlexContainerState : IMultiChildLayoutState
    {
        CrossAxisAlignment CrossAxisAlignment { get; }
        MainAxisAlignment MainAxisAlignment { get; }
        AxisSize MainAxisSize { get; }
        float Spacing { get; }
    }
}
