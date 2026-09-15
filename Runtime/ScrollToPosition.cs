namespace UniMob.UI
{
    public enum ScrollToPosition
    {
        /// <summary>
        ///     Aligns the leading edge of the item with the leading edge of the viewport.
        /// </summary>
        Start,

        /// <summary>
        ///     Aligns the center of the item with the center of the viewport.
        /// </summary>
        Center,

        /// <summary>
        ///     Aligns the trailing edge of the item with the trailing edge of the viewport.
        /// </summary>
        End,

        /// <summary>
        ///     Moves the least distance that shows the whole item: nowhere when it is already fully in
        ///     view, otherwise until the item's nearer edge meets the same edge of the viewport. An item
        ///     longer than the viewport aligns its leading edge.
        /// </summary>
        Nearest,
    }
}
