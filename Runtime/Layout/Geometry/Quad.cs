using System;
using UnityEngine;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// The four corners of a widget's box, in the same order Unity's
    /// <see cref="RectTransform.GetWorldCorners"/> produces them: bottom-left, top-left, top-right,
    /// bottom-right. The quadrilateral is not necessarily axis-aligned -- a rotated widget yields a
    /// rotated quad, which is why the corners are stored explicitly rather than as a single
    /// <see cref="Rect"/>.
    /// </summary>
    public readonly struct Quad : IEquatable<Quad>
    {
        public readonly Vector2 BottomLeft;
        public readonly Vector2 TopLeft;
        public readonly Vector2 TopRight;
        public readonly Vector2 BottomRight;

        public Quad(Vector2 bottomLeft, Vector2 topLeft, Vector2 topRight, Vector2 bottomRight)
        {
            BottomLeft = bottomLeft;
            TopLeft = topLeft;
            TopRight = topRight;
            BottomRight = bottomRight;
        }

        /// <summary>An all-zero quad, representing "no geometry".</summary>
        public static Quad Empty => default;

        /// <summary>Indexer following the <c>GetWorldCorners</c> order: 0=BL, 1=TL, 2=TR, 3=BR.</summary>
        public Vector2 this[int index] =>
            index switch
            {
                0 => BottomLeft,
                1 => TopLeft,
                2 => TopRight,
                3 => BottomRight,
                _ => throw new IndexOutOfRangeException(),
            };

        public bool Equals(Quad other) =>
            BottomLeft == other.BottomLeft
            && TopLeft == other.TopLeft
            && TopRight == other.TopRight
            && BottomRight == other.BottomRight;

        public override bool Equals(object obj) => obj is Quad other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(BottomLeft, TopLeft, TopRight, BottomRight);

        public static bool operator ==(Quad left, Quad right) => left.Equals(right);

        public static bool operator !=(Quad left, Quad right) => !left.Equals(right);
    }
}
