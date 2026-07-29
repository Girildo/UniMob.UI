#nullable enable
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// Places its child against another widget's measured box. <see cref="WidgetGeometryKey"/> tells
    /// you where a widget is; this puts something there.
    /// </summary>
    /// <remarks>
    /// Pure layout: it knows nothing about routes, navigators or overlays, so it works anywhere a
    /// constraint-based parent works. That is deliberate, and it is why this belongs in the layout
    /// library rather than beside the app's overlay plumbing.
    /// <para>
    /// ⚠ It must be given real constraints, which means a constraint-based parent. Given a parent that
    /// stretches instead of constraining, it measures as zero and nothing renders at all.
    /// </para>
    /// </remarks>
    public class AnchoredBox : SingleChildLayoutWidget
    {
        /// <summary>
        /// Stands in for the child until both boxes have been measured. Nothing is anchorable on the
        /// first frame, and a real child laid out at zero size both flashes and makes every flex row
        /// inside it report an overflow -- so the child is simply not built until there is somewhere
        /// to put it.
        /// </summary>
        internal Widget ChildOrPlaceholder(bool ready) =>
            ready && this.Child is { } child ? child : SizedBox.Shrink();

        /// <summary>
        /// The key attached to the widget being anchored to. Without one there is nowhere to put the
        /// child, so nothing is drawn at all.
        /// </summary>
        public WidgetGeometryKey? Anchor { get; set; }

        /// <summary>Which point of the anchor's box the child is placed at.</summary>
        public Alignment TargetAnchor { get; set; } = Alignment.BottomLeft;

        /// <summary>
        /// Which point of the CHILD meets <see cref="TargetAnchor"/>. Defaults to its top-left, so a
        /// menu hangs below-right of the point; a callout opening to one side wants an edge midpoint
        /// instead, which is why this is not simply the opposite of the target.
        /// </summary>
        public Alignment ChildAnchor { get; set; } = Alignment.TopLeft;

        /// <summary>Nudge applied after anchoring, in logical pixels (x right, y down).</summary>
        public Vector2 Offset { get; set; }

        /// <summary>
        /// Lets the placement flip to the opposite side of this axis -- mirroring
        /// <see cref="TargetAnchor"/>, <see cref="ChildAnchor"/> and <see cref="Offset"/> across it --
        /// when the child does not fit on the side it asked for and does fit on the other. Null
        /// places the child exactly as described.
        /// </summary>
        /// <remarks>
        /// This makes the three properties above a PREFERENCE rather than a promise, which is the
        /// point: a caller can state the side it wants for reasons of its own -- consistency with its
        /// siblings, usually -- without having to also be right about whether there is room for it.
        /// Being wrong now costs a flip instead of a callout laid over the thing it points at.
        /// <para>
        /// When neither side fits, the preferred one is kept and <see cref="KeepInsidePadding"/>
        /// slides it inside: a cramped screen should not also make the direction unpredictable.
        /// </para>
        /// </remarks>
        public Axis? FlipToFit { get; set; }

        /// <summary>
        /// Keeps the child inside this widget's own box when the anchor sits close enough to an edge
        /// to push it off, leaving this much padding. Null lets it overflow.
        /// </summary>
        /// <remarks>
        /// On by default: the whole point of this widget is that the position comes from a box the
        /// caller did not choose, so the caller cannot know whether the result fits. A tall callout
        /// hung off a small icon near the screen edge is the normal case, not the exotic one.
        /// </remarks>
        public float? KeepInsidePadding { get; set; } = 36f;

        /// <summary>
        /// Gives the child the anchor's measured width — how a dropdown menu lines up with the field
        /// that opened it, without either of them naming a number.
        /// </summary>
        public bool MatchAnchorWidth { get; set; }

        public override State CreateState() => new AnchoredBoxState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state) =>
            new RenderAnchoredBox((AnchoredBoxState)state);
    }

    public class AnchoredBoxState : ViewState<AnchoredBox>, IAnchoredBoxState
    {
        private readonly StateHolder child;

        public AnchoredBoxState()
        {
            // The child is selected here, not in the render object, because "there is nowhere to put
            // it yet" has to mean "do not build it" -- a render object can only choose a size, and
            // every size it could choose is either visible or an overflow.
            this.child = this.CreateChild(_ => this.Widget.ChildOrPlaceholder(this.Ready));
        }

        public override WidgetViewReference View =>
            WidgetViewReference.Resource("$$_Layout.SingleChildLayoutView");

        public IState Child => this.child.Value;

        /// <summary>
        /// Whether both boxes have been measured. Reading the two geometry atoms here -- inside the
        /// child's build function and, separately, inside the layout pass -- is what subscribes us to
        /// them, so the child appears and then follows without any further wiring.
        /// </summary>
        private bool Ready =>
            !this.AnchorGeometry.Equals(WidgetGeometry.Empty)
            && !this.SelfGeometry.Equals(WidgetGeometry.Empty);

        // Both are [Atom]s, read during the layout pass -- which runs inside a computed atom, so the
        // child re-places itself whenever either box moves. No key is needed for our own box: a
        // ViewState can locate itself.
        // An absent anchor reads as an unmeasured one, which keeps Ready false and leaves the
        // placeholder in place rather than throwing during layout.
        public WidgetGeometry AnchorGeometry =>
            this.Widget.Anchor?.GlobalGeometry ?? WidgetGeometry.Empty;

        public WidgetGeometry SelfGeometry => this.GlobalGeometry;

        public Alignment TargetAnchor => this.Widget.TargetAnchor;

        public Alignment ChildAnchor => this.Widget.ChildAnchor;

        public Vector2 Offset => this.Widget.Offset;

        public float? KeepInsidePadding => this.Widget.KeepInsidePadding;

        public bool MatchAnchorWidth => this.Widget.MatchAnchorWidth;

        public Axis? FlipToFit => this.Widget.FlipToFit;
    }
}
