#nullable enable
namespace UniMob.UI.Layout
{
    /// <summary>
    /// A state that presents exactly one child to the layout system.
    /// </summary>
    /// <remarks>
    /// Deliberately nothing but the child link. Geometry used to be bundled in here, which forced
    /// every implementor to answer questions it had no business answering -- a build-only wrapper owns
    /// no view and cannot report an on-screen box, yet had to implement one to be laid out. Local
    /// geometry is now read straight off the render object, and on-screen geometry belongs to
    /// <see cref="IViewState"/>.
    /// </remarks>
    public interface ISingleChildLayoutState : IState
    {
        IState? Child { get; }
    }
}
