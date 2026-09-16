using System;

namespace UniMob.UI.Internal
{
    /// <summary>
    ///     The configuration checks every virtualized scrollable makes of its own widget.
    /// </summary>
    internal static class ScrollableWidgetValidation
    {
        /// <summary>
        ///     Throws when eager children and lazy building are mixed, or when the item count does not
        ///     match the mode. <paramref name="widgetName" /> names the widget in the message, so the
        ///     author reads about the type they wrote.
        /// </summary>
        public static void ValidateChildrenMode(
            string widgetName,
            bool hasBuilder,
            bool hasChildren,
            int? itemCount
        )
        {
            if (hasBuilder && hasChildren)
                throw new InvalidOperationException(
                    $"{widgetName} cannot have both ItemBuilder and Children set -- use ItemBuilder+ItemCount "
                        + "for lazy building, or Children for eager building, not both."
                );

            if (hasBuilder && itemCount == null)
                throw new InvalidOperationException(
                    $"{widgetName}.ItemCount must be set when ItemBuilder is provided."
                );

            if (!hasBuilder && itemCount != null)
                throw new InvalidOperationException(
                    $"{widgetName}.ItemCount has no effect without ItemBuilder."
                );
        }
    }
}
