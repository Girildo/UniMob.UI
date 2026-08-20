namespace UniMob.UI
{
    /// <summary>
    ///     A state that presents an ordered set of children to the layout system. Like
    ///     <see cref="ISingleChildLayoutState"/>, this is only the child link: geometry is read off the
    ///     render object, and on-screen geometry belongs to <see cref="IViewState"/>.
    /// </summary>
    public interface IMultiChildLayoutState : IState
    {
        IState[] Children { get; }
    }
}
