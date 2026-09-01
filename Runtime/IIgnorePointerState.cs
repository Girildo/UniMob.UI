namespace UniMob.UI
{
    /// <summary>
    ///     A state that can make its subtree invisible to hit testing.
    /// </summary>
    /// <remarks>
    ///     What anything resolving a point to a widget has to ask on the way down. A subtree under an
    ///     ignoring state is laid out and painted like any other, so geometry alone says it can be
    ///     touched and only this says it cannot.
    /// </remarks>
    public interface IIgnorePointerState : ISingleChildLayoutState
    {
        /// <summary>
        ///     Whether pointer events pass through this subtree to whatever is behind it.
        /// </summary>
        bool Ignoring { get; }
    }
}
