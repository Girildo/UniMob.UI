namespace UniMob.UI
{
    internal interface IScrollingListState : IMultiChildLayoutState
    {
        ScrollController ScrollController { get; }
        Axis Axis { get; }

        public bool UseMask { get; }
        public MovementType MovementType { get; }
    }
}
