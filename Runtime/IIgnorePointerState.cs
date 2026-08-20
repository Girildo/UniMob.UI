namespace UniMob.UI
{
    internal interface IIgnorePointerState : ISingleChildLayoutState
    {
        bool Ignoring { get; }
    }
}
