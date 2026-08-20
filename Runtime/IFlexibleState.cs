namespace UniMob.UI
{
    /// <summary>
    /// A state that claims a share of the free space its flex parent has left over on the main axis.
    /// </summary>
    /// <remarks>
    /// <see cref="Flex"/> is a weight, not an extent: each flexible child gets free space in proportion
    /// to its own weight against the total. <see cref="Fit"/> decides whether the resulting extent is
    /// forced on the child or offered as a ceiling.
    /// </remarks>
    internal interface IFlexibleState : ISingleChildLayoutState
    {
        int Flex { get; }
        FlexFit Fit { get; }
    }
}
