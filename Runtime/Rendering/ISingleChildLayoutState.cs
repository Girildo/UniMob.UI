namespace UniMob.UI.Rendering
{
    /// <summary>
    /// A state that presents exactly one child to the layout system.
    /// </summary>
    /// <remarks>
    /// Deliberately nothing but the child link, so that anything with a child can be laid out.
    /// Bundling geometry in here would force every implementor to answer questions it has no business
    /// answering: a build-only wrapper owns no view and cannot report an on-screen box, yet would have
    /// to implement one merely to be laid out. Local geometry is read off the render object, and
    /// on-screen geometry belongs to <see cref="IViewState"/>.
    /// </remarks>
    public interface ISingleChildLayoutState : IState
    {
        IState? Child { get; }
    }
}
