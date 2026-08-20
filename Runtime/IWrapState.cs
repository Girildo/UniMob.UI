namespace UniMob.UI
{
    public interface IWrapState : IMultiChildLayoutState
    {
        Axis Direction { get; }
        float Spacing { get; }
        float RunSpacing { get; }

        MainAxisAlignment Alignment { get; }
        CrossAxisAlignment CrossAxisAlignment { get; }
        MainAxisAlignment RunAlignment { get; }
    }
}
