#nullable enable
using UniMob.UI;
using UniMob.UI.Layout;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    /// <summary>
    /// What <see cref="AnchoredBox"/> needs from its state to place a child against another widget's
    /// measured box. All of the geometry is reactive: the layout pass runs inside an atom, so reading
    /// it here is what makes the child follow the anchor.
    /// </summary>
    public interface IAnchoredBoxState : ISingleChildLayoutState
    {
        /// <summary><b>[Atom]</b> The box being anchored to, in canvas space.</summary>
        WidgetGeometry AnchorGeometry { get; }

        /// <summary><b>[Atom]</b> This widget's own box, in canvas space, to rebase the anchor into.</summary>
        WidgetGeometry SelfGeometry { get; }

        // The PREFERRED placement. It is mirrored across FlipToFit's axis when the child turns out
        // not to fit on that side -- a decision that belongs here rather than in the state, because
        // it is the one place the child's measured size exists.
        Alignment TargetAnchor { get; }
        Alignment ChildAnchor { get; }
        Vector2 Offset { get; }
        float? KeepInsidePadding { get; }
        bool MatchAnchorWidth { get; }

        /// <summary>The axis the placement may flip on, or null to place it exactly as described.</summary>
        Axis? FlipToFit { get; }
    }

    /// <summary>
    /// Places a single child at a point derived from another widget's box, and keeps it inside its own
    /// bounds.
    /// </summary>
    /// <remarks>
    /// A render object rather than a composition of positioning primitives because every part of the
    /// job needs the child's MEASURED size: meeting the anchor point with the child's own anchor,
    /// choosing a side it fits on, and sliding it back inside when the anchor sits near an edge. A
    /// general-purpose positioner cannot own any of that -- the position is computed, so only whatever
    /// computed it knows whether the result may land off-screen. Here one widget owns one policy, at
    /// the one moment the sizes exist.
    /// </remarks>
    public class RenderAnchoredBox : SingleChildRenderObject
    {
        private readonly IAnchoredBoxState state;

        public RenderAnchoredBox(IAnchoredBoxState state)
            : base(state)
        {
            this.state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            // Fill what we are given: this widget IS the space the child is placed within, so its own
            // size is the frame that "keep inside" refers to.
            var self = constraints.Constrain(
                new Vector2(float.PositiveInfinity, float.PositiveInfinity)
            );

            if (this.Child == null)
            {
                this.ChildSize = Vector2.zero;
                return self;
            }

            // Always the real constraints, even before the anchor is known. Sizing does not depend on
            // the anchor -- only placement does -- and squeezing the child to hide it would make every
            // flex row inside it report an overflow. The state substitutes a placeholder child while
            // there is nowhere to put it, so there is nothing to hide here.
            this.ChildSize = this.LayoutChild(this.Child, this.ChildConstraints(self));
            return self;
        }

        protected override void PerformPositioning()
        {
            if (this.Child == null || !this.TryGetGeometry(out var anchor, out var origin))
            {
                this.ChildPosition = Vector2.zero;
                return;
            }

            var flipped = this.ShouldFlip(anchor);
            var targetAnchor = Mirror(this.state.TargetAnchor, this.state.FlipToFit, flipped);
            var childAnchor = Mirror(this.state.ChildAnchor, this.state.FlipToFit, flipped);
            var offset = Mirror(this.state.Offset, this.state.FlipToFit, flipped);

            var target = anchor.GetPoint(targetAnchor);

            // Canvas space is y-UP from the bottom-left; a layout position is y-DOWN from the top
            // edge. The x term subtracts, the y term reverses -- that asymmetry is the whole
            // conversion, and getting it backwards is the classic bug here.
            var position = new Vector2(
                target.x - origin.x + offset.x,
                origin.y - target.y + offset.y
            );

            // The point marks where the child's OWN anchor goes, not its top-left, so back off by
            // however far that anchor sits into the child. ResolveOffset (not ToAnchor) because this
            // is layout space: Alignment carries two mappers and they disagree on Y.
            position -= childAnchor.ResolveOffset(this.ChildSize, Vector2.zero);

            if (this.state.KeepInsidePadding is { } padding)
            {
                position = new Vector2(
                    KeepInside(position.x, this.ChildSize.x, this.Size.x, padding),
                    KeepInside(position.y, this.ChildSize.y, this.Size.y, padding)
                );
            }

            this.ChildPosition = position;
        }

        /// <summary>
        /// Whether the preferred side has to give way, because the child does not fit there and does
        /// fit opposite.
        /// </summary>
        /// <remarks>
        /// Both conditions matter. A screen with room for neither keeps the caller's side and lets
        /// the clamp slide the child inside: being cramped should not also make the direction
        /// unpredictable, and flipping would trade one overlap for an identical one.
        /// </remarks>
        private bool ShouldFlip(WidgetGeometry anchor)
        {
            if (this.state.FlipToFit is not { } axis)
                return false;

            var self = this.state.SelfGeometry.Aabb;
            var box = anchor.Aabb;
            var padding = this.state.KeepInsidePadding ?? 0f;

            // The alignment component that names the side, the room on either side of the anchor
            // within our own frame, and how much of it the child needs.
            float side,
                towardsPositive,
                towardsNegative,
                needed;

            if (axis == Axis.Horizontal)
            {
                side = this.state.TargetAnchor.X;
                // Alignment +X is east and canvas +x is east: the two agree, nothing reverses.
                towardsPositive = self.xMax - box.xMax;
                towardsNegative = box.xMin - self.xMin;
                needed = this.ChildSize.x + Mathf.Abs(this.state.Offset.x) + padding;
            }
            else
            {
                side = this.state.TargetAnchor.Y;
                // Alignment +Y is SOUTH while canvas +y points north, so the two room terms swap.
                // This is the disagreement between Alignment's two mappers, and it is why the
                // vertical half of the flip preview is the half worth reading.
                towardsPositive = box.yMin - self.yMin;
                towardsNegative = self.yMax - box.yMax;
                needed = this.ChildSize.y + Mathf.Abs(this.state.Offset.y) + padding;
            }

            // A centred anchor point names no side, so there is nothing to flip it away from.
            if (side == 0f)
                return false;

            var preferred = side > 0f ? towardsPositive : towardsNegative;
            var opposite = side > 0f ? towardsNegative : towardsPositive;

            return preferred < needed && opposite >= needed;
        }

        private static Alignment Mirror(Alignment alignment, Axis? axis, bool flipped) =>
            !flipped ? alignment
            : axis == Axis.Horizontal ? new Alignment(-alignment.X, alignment.Y)
            : new Alignment(alignment.X, -alignment.Y);

        private static Vector2 Mirror(Vector2 offset, Axis? axis, bool flipped) =>
            !flipped ? offset
            : axis == Axis.Horizontal ? new Vector2(-offset.x, offset.y)
            : new Vector2(offset.x, -offset.y);

        private LayoutConstraints ChildConstraints(Vector2 self)
        {
            var width = this.state.MatchAnchorWidth
                ? (float?)this.state.AnchorGeometry.Size.x
                : null;

            if (this.state.KeepInsidePadding is not { } padding)
            {
                // Free to be any size, so the child hugs its content and may overflow -- the caller
                // asked for that by switching keep-inside off.
                return new LayoutConstraints(
                    width ?? 0f,
                    0f,
                    width ?? float.PositiveInfinity,
                    float.PositiveInfinity
                );
            }

            // Cap at what we can actually show, so that keeping the child inside is always possible.
            // A child bigger than its frame has nowhere legal to sit and would be parked against one
            // edge, overflowing the far one.
            //
            // This is a bound, not a size: content that sizes itself is untouched. Content that takes
            // whatever it is offered -- a Column stretching its cross axis -- takes ALL of it, which
            // is a wide callout rather than a broken one, and such content should state its own width
            // at the call site.
            var maxWidth = Mathf.Max(0f, self.x - (padding * 2f));
            var maxHeight = Mathf.Max(0f, self.y - (padding * 2f));

            return new LayoutConstraints(
                width ?? 0f,
                0f,
                width.HasValue ? Mathf.Min(width.Value, maxWidth) : maxWidth,
                maxHeight
            );
        }

        private bool TryGetGeometry(out WidgetGeometry anchor, out Vector2 origin)
        {
            anchor = this.state.AnchorGeometry;
            var self = this.state.SelfGeometry;

            if (anchor.Equals(WidgetGeometry.Empty) || self.Equals(WidgetGeometry.Empty))
            {
                origin = Vector2.zero;
                return false;
            }

            origin = self.GetPoint(Alignment.TopLeft);
            return true;
        }

        private static float KeepInside(
            float position,
            float childExtent,
            float parentExtent,
            float padding
        )
        {
            var max = parentExtent - childExtent - padding;

            // Too big to fit even after the cap above (an unbounded frame, say): park it at the low
            // edge. Clamping to `max` would push the child PAST the near edge to bring its far edge
            // in, which reads as a positioning bug rather than as the overflow it is.
            if (max < padding)
                return padding;

            return Mathf.Clamp(position, padding, max);
        }
    }
}
