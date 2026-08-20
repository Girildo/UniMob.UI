namespace UniMob.UI
{
    public enum ImageFit
    {
        /// <summary>
        /// Fill the target box by distorting the image's aspect ratio.
        /// </summary>
        Fill,

        /// <summary>
        /// As large as possible while maintaining aspect ratio and staying inside the box.
        /// </summary>
        Contain,

        /// <summary>
        /// Fill the target box completely, maintaining aspect ratio. Will crop the image if necessary.
        /// </summary>
        Cover,

        /// <summary>
        /// Make sure the full width of the image is shown. May crop vertically or leave empty vertical space.
        /// </summary>
        FitWidth,

        /// <summary>
        /// Make sure the full height of the image is shown. May crop horizontally or leave empty horizontal space.
        /// </summary>
        FitHeight,

        /// <summary>
        /// Do not scale the image. Centers it inside the box.
        /// </summary>
        None,

        /// <summary>
        /// Scale down to fit inside the box, but do not scale up if the image is smaller than the box.
        /// </summary>
        ScaleDown,
    }
}
