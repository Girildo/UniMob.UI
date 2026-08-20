namespace UniMob.UI.Layout
{
    public static class ConstraintsExtensions
    {
        /// <summary>
        /// Return the minimum constraint along the given axis.
        /// </summary>
        public static float MinAlongAxis(this LayoutConstraints constraints, Axis axis)
        {
            return axis == Axis.Horizontal ? constraints.MinWidth : constraints.MinHeight;
        }

        /// <summary>
        /// Return the maximum constraint along the given axis.
        /// </summary>
        public static float MaxAlongAxis(this LayoutConstraints constraints, Axis axis)
        {
            return axis == Axis.Horizontal ? constraints.MaxWidth : constraints.MaxHeight;
        }
    }
}
