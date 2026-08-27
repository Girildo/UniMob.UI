namespace UniMob.UI
{
    public record struct RectPadding
    {
        public static RectPadding Zero => All(0);

        private RectPadding(float left, float top, float right, float bottom)
        {
            this.Left = left;
            this.Top = top;
            this.Right = right;
            this.Bottom = bottom;
        }

        public readonly float Left { get; }
        public readonly float Right { get; }
        public readonly float Top { get; }
        public readonly float Bottom { get; }

        public readonly float Horizontal => Left + Right;
        public readonly float Vertical => Top + Bottom;

        /// <summary>
        /// Creates a new RectPadding with the specified horizontal and vertical padding.
        /// </summary>
        public static RectPadding Symmetric(float horizontal, float vertical) =>
            FromLTRB(horizontal, vertical, horizontal, vertical);

        /// <summary>
        /// Creates a new RectPadding with the same padding on all sides.
        /// </summary>
        public static RectPadding All(float padding) => Symmetric(padding, padding);

        /// <summary>
        /// Creates a new RectPadding from the specified left, top, right, and bottom values.
        /// </summary>
        public static RectPadding FromLTRB(float left, float top, float right, float bottom) =>
            new(left, top, right, bottom);

        /// <summary>
        /// Creates a new RectPadding with only the specified sides set, the rest left at zero.
        /// </summary>
        public static RectPadding Only(
            float left = 0,
            float right = 0,
            float top = 0,
            float bottom = 0
        ) => FromLTRB(left, top, right, bottom);

        public override string ToString()
        {
            if (Left == Right && Right == Top && Top == Bottom)
                return $"RectPadding: {Left}";
            else if (Left == Right && Top == Bottom)
                return $"RectPadding: H: {Left}, V: {Top}";
            else
                return $"RectPadding: L: {Left}, R: {Right}, T: {Top}, B: {Bottom}";
        }
    }
}
