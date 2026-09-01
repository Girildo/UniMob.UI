namespace UniMob.UI
{
    /// <summary>
    ///     A state that scrolls its children along one axis.
    /// </summary>
    /// <remarks>
    ///     How the list is driven, not what it holds. The children are the realized window, so an item
    ///     outside it is not in the tree at all, and reaching it means moving the list first through
    ///     <see cref="ScrollController"/>.
    /// </remarks>
    public interface IScrollingListState : IMultiChildLayoutState
    {
        /// <summary>Reads and moves the scroll position.</summary>
        ScrollController ScrollController { get; }

        /// <summary>The axis the children are laid out and scrolled along.</summary>
        Axis Axis { get; }

        /// <summary>Whether the viewport clips whatever falls outside it.</summary>
        bool UseMask { get; }

        /// <summary>How the content behaves at the ends of its range.</summary>
        MovementType MovementType { get; }
    }
}
