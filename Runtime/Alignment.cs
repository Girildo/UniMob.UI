using UnityEngine;

namespace UniMob.UI
{
    public struct Alignment
    {
        public float X { get; }
        public float Y { get; }

        public Alignment(float x, float y)
        {
            X = x;
            Y = y;
        }

        public Alignment WithTop() => new Alignment(X, TopCenter.Y);

        public Alignment WithCenterY() => new Alignment(X, Center.Y);

        public Alignment WithLeft() => new Alignment(CenterLeft.X, Y);

        public Alignment WithRight() => new Alignment(CenterRight.X, Y);

        public Alignment WithCenterX() => new Alignment(Center.X, Y);

        /// <summary>
        /// This alignment as a 0..1 fraction in UNITY canvas space, whose origin is bottom-left and
        /// whose +y points UP -- so Top is 1. That is the convention of
        /// <see cref="RectTransform.anchorMin"/>/<c>anchorMax</c>/<c>pivot</c> and of the canvas-space
        /// boxes reported by <c>WidgetGeometry</c>, which are its only callers.
        /// <para>
        /// ⚠ Not interchangeable with <see cref="ResolveOffset"/>: LAYOUT space runs y-DOWN from a
        /// parent's top-left, where Top is 0. Positioning a child inside a render object is layout
        /// space and wants ResolveOffset; touching a RectTransform or a canvas-space point is this.
        /// </para>
        /// </summary>
        public Vector2 ToAnchor() => new Vector2(X * 0.5f + 0.5f, -Y * 0.5f + 0.5f);

        /// <summary>
        /// Resolves the top-left offset at which a child of <paramref name="childSize"/> should be placed
        /// within an area of <paramref name="availableSize"/> to honor this alignment.
        /// Works in LAYOUT space: y runs DOWN from the parent's top-left, so Top is 0 (contrast
        /// <see cref="ToAnchor"/>, which is the y-up canvas convention).
        /// </summary>
        public Vector2 ResolveOffset(Vector2 availableSize, Vector2 childSize) =>
            new Vector2(
                (availableSize.x - childSize.x) * (X * 0.5f + 0.5f),
                (availableSize.y - childSize.y) * (Y * 0.5f + 0.5f)
            );

        /// <summary>
        /// The center point along the bottom edge.
        /// </summary>
        public static readonly Alignment BottomCenter = new Alignment(0.0f, 1.0f);

        /// <summary>
        ///  The bottom left corner.
        /// </summary>
        public static readonly Alignment BottomLeft = new Alignment(-1.0f, 1.0f);

        /// <summary>
        /// The bottom right corner.
        /// </summary>
        public static readonly Alignment BottomRight = new Alignment(1.0f, 1.0f);

        /// <summary>
        /// The center point, both horizontally and vertically.
        /// </summary>
        public static readonly Alignment Center = new Alignment(0.0f, 0.0f);

        /// <summary>
        /// The center point along the left edge.
        /// </summary>
        public static readonly Alignment CenterLeft = new Alignment(-1.0f, 0.0f);

        /// <summary>
        /// The center point along the right edge.
        /// </summary>
        public static readonly Alignment CenterRight = new Alignment(1.0f, 0.0f);

        /// <summary>
        /// The center point along the top edge.
        /// </summary>
        public static readonly Alignment TopCenter = new Alignment(0.0f, -1.0f);

        /// <summary>
        /// The top left corner.
        /// </summary>
        public static readonly Alignment TopLeft = new Alignment(-1.0f, -1.0f);

        /// <summary>
        /// The top right corner.
        /// </summary>
        public static readonly Alignment TopRight = new Alignment(1.0f, -1.0f);
    }
}
