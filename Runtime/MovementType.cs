namespace UniMob.UI
{
    public enum MovementType
    {
        /// <summary>
        /// The content can be scrolled indefinitely in either direction.
        /// </summary>
        Unrestricted,
        /// <summary>
        /// The content can be scrolled beyond its bounds, but it will snap back when released.
        /// </summary>
        Elastic,
        /// <summary>
        /// The content cannot be scrolled beyond its bounds. It will stop immediately at the edge.
        /// If the content is smaller than the viewport, it will not be scrollable at all.
        /// </summary>
        Clamped
    }
}
