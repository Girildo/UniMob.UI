using System;
using UnityEngine;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// A snapshot of a widget's rendered bounding box, analogous to Flutter's
    /// <c>RenderBox.localToGlobal(Offset.zero) &amp; size</c>. The box is captured in two spaces:
    /// <see cref="CanvasSpace"/> (DPI-independent UI units, with the root canvas scale removed -- what
    /// you position other widgets against) and <see cref="WorldSpace"/> (raw Unity world/screen units).
    /// Canvas space has its origin at the bottom-left of the screen, with +x right and +y up.
    /// </summary>
    public readonly struct WidgetGeometry : IEquatable<WidgetGeometry>
    {
        /// <summary>Corners in canvas space (root-canvas scale removed). Order: BL, TL, TR, BR.</summary>
        public readonly Quad CanvasSpace;

        /// <summary>
        /// Corners in raw Unity world space. Identical to <see cref="CanvasSpace"/> when the root
        /// canvas scale is 1.
        /// </summary>
        public readonly Quad WorldSpace;

        public WidgetGeometry(Quad canvasSpace, Quad worldSpace)
        {
            CanvasSpace = canvasSpace;
            WorldSpace = worldSpace;
        }

        /// <summary>The zero geometry, reported when a widget is not currently mounted to a view.</summary>
        public static readonly WidgetGeometry Empty = default;

        /// <summary>The axis-aligned bounding box in canvas space.</summary>
        public Rect Aabb => Rect.MinMaxRect(
            CanvasSpace.BottomLeft.x, CanvasSpace.BottomLeft.y,
            CanvasSpace.TopRight.x, CanvasSpace.TopRight.y);

        /// <summary>The size of the axis-aligned bounding box in canvas space.</summary>
        public Vector2 Size => Aabb.size;

        /// <summary>
        /// The absolute canvas-space coordinate of a given alignment point on this box (e.g.
        /// <see cref="Alignment.BottomCenter"/> is the middle of the bottom edge). Useful for anchoring
        /// a follower to a specific edge/corner of the tracked widget.
        /// </summary>
        public Vector2 GetPoint(Alignment alignment) => CanvasSpace.BottomLeft + Aabb.size * alignment.ToAnchor();

        // Equality is by canvas-space corners: world space is derived from the same transform, and
        // canvas space is what consumers position against, so comparing it is sufficient for change
        // detection. This is what lets the reactive GlobalGeometry atom suppress no-op updates so a
        // follower rebuilds only when the box actually moves or resizes.
        public bool Equals(WidgetGeometry other) => CanvasSpace.Equals(other.CanvasSpace);

        public override bool Equals(object obj) => obj is WidgetGeometry other && Equals(other);

        public override int GetHashCode() => CanvasSpace.GetHashCode();

        public static bool operator ==(WidgetGeometry left, WidgetGeometry right) => left.Equals(right);

        public static bool operator !=(WidgetGeometry left, WidgetGeometry right) => !left.Equals(right);
    }
}
