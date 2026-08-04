#nullable enable
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    /// <summary>
    /// Represents a render object that manages a single child and provides layout and rendering behavior for it.
    /// </summary>
    /// <remarks>This abstract class serves as a base for render objects that are responsible for managing a
    /// single child. It provides properties to access the child's size and position, as well as methods to calculate
    /// intrinsic dimensions based on the child's layout. Subclasses are expected to define specific layout and
    /// rendering behavior for the child.</remarks>
    public abstract class SingleChildRenderObject : RenderObject, ISingleChildRenderObject
    {
        private readonly ISingleChildLayoutState _state;

        // TODO(layout): ChildSize/ChildPosition are set manually by each PerformSizing/PerformPositioning
        // override (see RenderProxy, RenderPadding, RenderConstrainedBox, RenderPositionedBox,
        // RenderAspectRatio, RenderIntrinsicSize). Forgetting to assign ChildSize after laying out Child
        // silently strands the child's RectTransform at (0,0) -- see the RenderIntrinsicSize bug this
        // TODO was added for, and the identical latent gap in RenderConstrainedBox's no-child branch.
        // A shadowed/overridden LayoutChild that auto-writes ChildSize was tried and reverted: it just
        // relocates the smell (LayoutChild stops being able to run without committing ChildSize, which
        // rules out ever adding a speculative/non-committing measurement later). The real fix is to split
        // PerformSizing into pure hooks -- GetChildConstraints(constraints) and
        // ComputeSize(constraints, childSize) -- so the base class is the only place that ever calls
        // LayoutChild/writes ChildSize, exactly once, and subclasses stay side-effect-free. That's a
        // breaking change for library consumers who've subclassed these render objects, so it's deferred.
        public Vector2 ChildSize { get; protected set; }
        public Vector2 ChildPosition { get; protected set; }

        public LayoutInfo ChildLayout => new LayoutInfo
        {
            Size = ChildSize,
            Position = ChildPosition
        };


        protected IState? Child => _state.Child;

        protected SingleChildRenderObject(ISingleChildLayoutState state) : base(state)
        {
            _state = state;
        }

        /// <summary>
        ///     Adds the geometric check to the postcondition: after positioning, the child may not lie
        ///     outside this box unless overhanging is what this render object is for.
        /// </summary>
        protected override void ValidateLayout(LayoutConstraints constraints)
        {
            base.ValidateLayout(constraints);

#if UNIMOB_UI_DIAGNOSTICS
            // Silent once anything else has spoken this pass: this check is the net under the others,
            // not a second opinion on what they already caught.
            if (Child is null || this.ChildrenMayOverhang || this.HasLayoutIssue)
            {
                return;
            }

            var axes = Overhang(ChildPosition, ChildSize, PeekSize(), out var amount);

            if (axes != LayoutAxes.None)
            {
                ReportChildOutOfBounds(0, axes, amount, SomethingHereIsBiggerThanItsBox);
            }
#endif
        }

        /// <summary>
        ///     The one child, at index 0, so a report from here can name a culprit the same way a
        ///     multi-child one does.
        /// </summary>
        protected override IState? ChildAt(int index) => index == 0 ? Child : null;

        protected override float ComputeIntrinsicWidth(float height)
        {
            if (Child != null)
            {
                return Child.RenderObject.GetIntrinsicWidth(height);
            }
            return 0;
        }

        protected override float ComputeIntrinsicHeight(float width)
        {
            if (Child != null)
            {
                return Child.RenderObject.GetIntrinsicHeight(width);
            }
            return 0;
        }
    }
}