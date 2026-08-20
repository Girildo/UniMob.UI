namespace UniMob.UI
{
    internal interface IOpacityState : ISingleChildLayoutState
    {
        IAnimation<float> OpacityValue { get; }
    }
}
