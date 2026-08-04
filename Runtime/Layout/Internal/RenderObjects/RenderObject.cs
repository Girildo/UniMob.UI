#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNIMOB_UI_FORCE_DIAGNOSTICS
#define UNIMOB_UI_DIAGNOSTICS
#endif

using System;
using System.Diagnostics;
using JetBrains.Annotations;
using UniMob.UI.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    /// <summary>
    ///     Performs all layout calculation for a LayoutWidget, and owns the reactive state driving it:
    ///     the constraints it was last given, and a memoized pass over them. Independent of
    ///     MonoBehaviour and of Unity's rendering pipeline, but not of UniMob -- it holds atoms on a
    ///     <see cref="Lifetime"/>, so it is neither stateless nor free to construct against a dead one.
    /// </summary>
    /// <remarks>
    ///     A render object owns its own layout: the constraints it was last given, and a memoized pass
    ///     over <see cref="PerformSizing"/> plus <see cref="PerformPositioning"/>. Because the memo
    ///     lives here rather than on whatever state happens to reach this object, a render object is
    ///     laid out once per set of constraints no matter how many states can see it.
    ///     <para>
    ///         Layout is a push, and is named as one. A child's constraints are an output of the
    ///         parent's algorithm -- <c>RenderFlex</c> has to measure its inflexible children before it
    ///         can constrain the flexible ones -- so there is no cheap per-child constraints getter that
    ///         could be pulled instead. The atoms memoize the result of that push; they do not reverse
    ///         its direction.
    ///     </para>
    /// </remarks>
    public abstract class RenderObject
    {
        private readonly MutableAtom<LayoutConstraints?> _constraints = Atom.Value(
            default(LayoutConstraints?)
        );

        private readonly Atom<(Vector2 size, int version)> _trackedLayout;
        private readonly Atom<Vector2> _trackedSize;

        private int _layoutVersion = int.MinValue;

#if UNITY_EDITOR
        // Plain fields, not derived from the constraints atom: these are read from a diagnostic that
        // must not register a dependency on anything. Compiled out entirely rather than left as a
        // dead store, because their one reader is, and Layout() is the hottest method in the system.
        private bool _everLaidOut;
        private bool _reportedNeverLaidOut;
#endif

#if UNIMOB_UI_DIAGNOSTICS
        // The latch. Two bitmasks over LayoutIssueCode: what this pass noticed, and what has already
        // been said. Frame counters are not usable -- Time.frameCount does not advance in EditMode
        // tests -- and a dictionary would cost something on the healthy path.
        private int _seenThisPass;
        private int _reported;
#endif

        protected Lifetime Lifetime { get; }

        /// <summary>
        ///     The single state this render object belongs to.
        /// </summary>
        /// <remarks>
        ///     Deliberately <c>protected</c>. Every reader of <c>.RenderObject</c> in this package
        ///     already holds the corresponding state, and not one goes the other way. A public back-edge
        ///     from the layout graph into the element tree would invite <c>Owner.Size</c> inside a
        ///     sizing pass, which is exactly the aliasing that moving layout onto the render object
        ///     removed. Widening it later breaks nobody; narrowing it would.
        /// </remarks>
        [NotNull]
        protected IState Owner { get; }

        protected RenderObject([NotNull] IState owner)
        {
            this.Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.Lifetime = owner.StateLifetime;

            _trackedLayout = Atom.Computed(this.Lifetime, PerformLayout);
            _trackedSize = Atom.Computed(this.Lifetime, () => _trackedLayout.Get().size);
        }

        /// <summary>
        ///     <b>Level-triggered.</b> True for exactly as long as a fault is happening here, which is
        ///     not the same thing as the console having said so: the log is edge-triggered and reports
        ///     that a fault <i>started</i>.
        /// </summary>
        public bool HasLayoutIssue
        {
            get
            {
#if UNIMOB_UI_DIAGNOSTICS
                return _seenThisPass != 0;
#else
                return false;
#endif
            }
        }

        private Vector2 _size;

        /// <summary>
        ///     The size as of the last pass, read inertly: no subscription, and no recomputation of
        ///     a pass that was invalidated since.
        /// </summary>
        /// <remarks>
        ///     For readers that must not drive anything: diagnostics and debugger displays evaluated
        ///     mid-layout, and fixtures asserting immediately after a synchronous <see cref="Layout"/>.
        ///     <c>Atom.NoWatch</c> is not a substitute here -- it stops the subscription, but a
        ///     tracked accessor still recomputes a dirty pass, and a report that lays a subtree out
        ///     mid-report is a fault of its own. Anything that wants a size that stays current wants
        ///     <see cref="WatchedSize"/> instead.
        /// </remarks>
        public Vector2 PeekSize() => _size;

        /// <summary>
        ///     The constraints this render object was last laid out against, or <c>null</c> if it has
        ///     never been laid out. Read-only: constraints are written only by <see cref="Layout"/>.
        /// </summary>
        public LayoutConstraints? Constraints => _constraints.Value;

        /// <summary>
        ///     Lays this render object out under <paramref name="constraints"/> and returns the
        ///     resulting size.
        /// </summary>
        /// <remarks>
        ///     <list type="bullet">
        ///         <item>
        ///             <description>The RenderObject works exclusively in a logical, top-left coordinate system:</description>
        ///         </item>
        ///         <item>
        ///             <c>RenderObject.PeekSize()</c> (Vector2): The final, calculated width and height of the widget.
        ///         </item>
        ///         <item>
        ///             <c>RenderObject.ChildrenLayout[i].CornerPosition</c> (Vector2):
        ///             The <c>(x, y)</c> coordinate of the top-left corner of the i-th child's bounding box,
        ///             relative to the parent's top-left corner
        ///         </item>
        ///     </list>
        ///     <para>
        ///         Repeating the call with unchanged constraints is free: the write is dropped by the
        ///         atom's equality check and the memoized size is returned without re-running the pass.
        ///     </para>
        /// </remarks>
        public Vector2 Layout(LayoutConstraints constraints)
        {
            if (this.Lifetime.IsDisposed)
                return Vector2.zero;

            // The write is a mutation performed from inside whatever computation is laying this object
            // out, so it must not register as a dependency of that computation, nor be reported as an
            // invalidation from within a tracked scope.
            using (Atom.NoWatch)
            {
                _constraints.Value = constraints;
            }

#if UNITY_EDITOR
            _everLaidOut = true;
#endif

            return _trackedSize.Get();
        }

        /// <summary>
        ///     <b>[Atom]</b> Observes this object's size, invalidating subscribers only when the size
        ///     actually changes.
        /// </summary>
        /// <remarks>
        ///     Use this when measuring something purely to learn how big it is. It decouples the caller
        ///     from a subtree that merely repositions its own contents without changing its own size,
        ///     which would otherwise cascade a relayout up the whole ancestor chain on something as
        ///     routine as a nested list scrolling.
        /// </remarks>
        public Vector2 WatchedSize()
        {
            if (this.Lifetime.IsDisposed)
                return Vector2.zero;
            return _trackedSize.Get();
        }

        /// <summary>
        ///     <b>[Atom]</b> Observes layout activity, invalidating subscribers on <i>every</i> pass
        ///     even when the size is unchanged.
        /// </summary>
        /// <remarks>
        ///     This is what views need: they write both size and position, so a pass that only moved
        ///     children still has to re-run them. Prefer <see cref="WatchedSize"/> anywhere the size is
        ///     the only thing that matters.
        /// </remarks>
        public Vector2 WatchLayout()
        {
            if (this.Lifetime.IsDisposed)
                return Vector2.zero;

#if UNITY_EDITOR
            // The view layer is the one caller that cannot tolerate "not yet": it is about to stamp
            // this size onto a RectTransform, and the answer here is a zero that looks like a
            // measurement. Reported here rather than in the pass itself because measuring something
            // before anything has laid it out is legitimate -- a geometry key read on the first
            // frame, say -- whereas painting it is not.
            //
            // Nothing constrains a render object ambiently any more: a state whose parent never
            // pushes is not laid out small, it is not laid out at all, and stays zero forever.
            if (!_everLaidOut && !_reportedNeverLaidOut)
            {
                _reportedNeverLaidOut = true;
                Debug.LogError(
                    $"{GetType().Name} is being rendered before anything laid it out, so its size is "
                        + "zero rather than measured. Its parent's layout pass never reached it: a "
                        + "custom render object whose sizing pass does not call LayoutChild for every "
                        + "child is the usual cause."
                );
            }
#endif

            return _trackedLayout.Get().size;
        }

        /// <summary>
        ///     Forces the next layout call to recompute rather than serve the memo.
        /// </summary>
        /// <remarks>
        ///     Needed because not every input to a sizing pass is an atom: a state's widget is reached
        ///     through plain accessors, so replacing the widget changes the answer without invalidating
        ///     anything. State.Update calls this.
        /// </remarks>
        public void InvalidateLayout()
        {
            _trackedLayout.Invalidate();
        }

        private (Vector2 size, int version) PerformLayout()
        {
            if (this.Lifetime.IsDisposed)
            {
                return (_size, _layoutVersion);
            }

            var constraints = _constraints.Value;
            if (!constraints.HasValue)
            {
                // Never laid out. Nothing can be computed yet, and inventing a value here would be
                // indistinguishable from having been laid out at that size.
                return (_size, _layoutVersion);
            }

#if UNIMOB_UI_DIAGNOSTICS
            _seenThisPass = 0;
#endif

            // Phase 1: Perform this widget's own size.
            _size = PerformSizing(constraints.Value);

            // Phase 2: Perform layout for children.
            PerformPositioning(_size);

#if UNIMOB_UI_DIAGNOSTICS
            ValidateLayout(constraints.Value);

            // Re-arm anything that stopped happening. Only the condition clearing does this: a
            // changing culprit or a changing amount is the same fault, and re-reporting it every pass
            // is the flood this latch exists to stop.
            _reported &= _seenThisPass;
#endif

            // A pass ran, and subscribers to WatchLayout must re-run even if the size is unchanged,
            // so the version always moves.
            return (_size, _layoutVersion = (_layoutVersion + 1) % int.MaxValue);
        }

        /// <summary>
        ///     Compute the size of the widget based on the provided layout constraints.
        ///     This method is called during the layout pass to determine the size of the widget.
        /// </summary>
        /// <param name="constraints"> The constraints imposed by the parent</param>
        /// <returns> A <see cref="Vector2" /> containing <c>(width, height)</c> </returns>
        protected abstract Vector2 PerformSizing(LayoutConstraints constraints);

        /// <summary>
        ///     Performs the positioning of the widget and its children.
        /// </summary>
        /// <remarks>
        ///     Called after the sizing phase with <paramref name="size"/>, the size this pass just
        ///     committed. The parameter is the only channel between the phases: sizing cannot read a
        ///     value that does not exist yet, and positioning does not reach for ambient state that
        ///     could be stale.
        ///     <para>
        ///         After this method is called, the positions of the children should be set in such a way that
        ///         the children's positions are known relative to the parent's top-left corner.
        ///     </para>
        ///     <para>
        ///         In particular:
        ///         <list type="bullet">
        ///             <item>
        ///                 Origin <c>(0,0)</c>: The top-left corner of the parent widget's available layout area.
        ///             </item>
        ///             <item>
        ///                 X-Axis: Positive is to the right. Y-Axis: Positive is downwards.
        ///             </item>
        ///         </list>
        ///     </para>
        /// </remarks>
        protected abstract void PerformPositioning(Vector2 size);

        /// <summary>
        ///     A helper method to handle the boilerplate of laying out a child.
        ///     It performs the layout and returns the child's final calculated size.
        /// </summary>
        /// <param name="child">The child state to layout.</param>
        /// <param name="constraints">The constraints to apply to the child.</param>
        /// <returns>The final size of the child after layout.</returns>
        protected Vector2 LayoutChild(IState child, LayoutConstraints constraints)
        {
            if (this.Lifetime.IsDisposed)
                return Vector2.zero;
            if (child is null)
                return Vector2.zero;

            // Layout, not WatchLayout: this returns the child's size with the equality cutoff, so a
            // parent's sizing pass is not dragged into re-running every time the child's subtree merely
            // repositions itself internally (a nested ScrollList scrolling, say).
            return child.RenderObject.Layout(constraints);
        }

        /// <summary>
        ///     Calculates the widget's preferred width given a specific height.
        /// </summary>
        public float GetIntrinsicWidth(float height)
        {
            if (this.Lifetime.IsDisposed)
                return 0;
            return ComputeIntrinsicWidth(height);
        }

        protected abstract float ComputeIntrinsicWidth(float height);

        /// <summary>
        ///     Calculates the widget's preferred height given a specific width.
        /// </summary>
        public float GetIntrinsicHeight(float width)
        {
            if (this.Lifetime.IsDisposed)
                return 0;
            return ComputeIntrinsicHeight(width);
        }

        protected abstract float ComputeIntrinsicHeight(float width);

        // -- Reporting --------------------------------------------------------------------------
        //
        // Report through these, never Debug.Log at the call site. Being void and [Conditional], a
        // release player drops the call *and its argument expressions*, which is what makes an
        // argument like "find the largest inflexible child" free when nothing is wrong. It is also
        // the one place Atom.NoWatch can be applied on behalf of every site at once: a report reads
        // the tree, and the tree is atoms, and all of this runs inside a layout computation.
        //
        // Three symbols rather than an opt-out, because [Conditional] attributes OR together and
        // there is no "and not X". The third has to be a project-wide Player Settings define, since
        // [Conditional] is evaluated in the caller's compilation -- which is also why it correctly
        // covers a render object living outside this package.

        /// <summary>Children needed more room than there was.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNIMOB_UI_FORCE_DIAGNOSTICS")]
        protected void ReportOverflow(
            LayoutAxes axes,
            float amount,
            int culpritIndex,
            string remedy
        )
        {
#if UNIMOB_UI_DIAGNOSTICS
            // The tolerance band lives here rather than in the algorithm, because it is a threshold
            // for saying something, not for doing something: the free space is clamped either way.
            if (amount <= LayoutConstants.OverflowTolerance)
            {
                return;
            }

            MarkCulprit(culpritIndex, LayoutIssueCode.Overflow);

            // No size: this fires from inside PerformSizing, and Size is not written until that
            // returns -- so passing it here reports the PREVIOUS pass's answer as though it were this
            // one's. Seen in the wild as a row printing 1098 while being laid out at 258.
            Emit(
                new LayoutIssue(
                    LayoutIssueCode.Overflow,
                    this.Owner,
                    axes,
                    remedy,
                    ChildAt(culpritIndex),
                    _constraints.Value,
                    amount: amount
                )
            );
#endif
        }

        /// <summary>
        ///     This render object's own content needs more room than it was given, on an axis where the
        ///     shortfall is a real number rather than a request to fill.
        /// </summary>
        /// <remarks>
        ///     Call it immediately before <see cref="LayoutConstraints.Constrain"/>, passing what the
        ///     content actually wanted. That call is where the fault becomes undetectable: it is
        ///     <c>Mathf.Clamp</c>, so the shortfall exists for the lifetime of one expression and nothing
        ///     downstream -- including <see cref="ValidateLayout"/>, which runs after and sees only the
        ///     clamped answer -- can recover it.
        ///     <para>
        ///         <b>An infinite desire is not a fault.</b> Asking for infinity and being clamped to the
        ///         maximum is how a render object says "fill", and it is the single most common thing
        ///         they do. Only a <i>finite</i> desire beyond the maximum means content that will be
        ///         drawn outside its box, because this layout layer does not clip.
        ///     </para>
        /// </remarks>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNIMOB_UI_FORCE_DIAGNOSTICS")]
        /// <param name="considered">
        ///     The axes on which falling short is a fault at all. Defaults to both. Narrow it where an
        ///     axis has a legitimate reason to come up short -- a text that was told not to wrap is
        ///     meant to be too wide for its box -- so that the report keeps meaning something.
        /// </param>
        protected void ReportContentOverflow(
            LayoutConstraints constraints,
            Vector2 desired,
            string remedy,
            LayoutAxes considered = LayoutAxes.Both
        )
        {
#if UNIMOB_UI_DIAGNOSTICS
            var axes = LayoutAxes.None;
            var amount = 0f;

            // Subtracting an infinite maximum gives negative infinity, and a non-finite desire fails
            // IsFinite, so both of the legal shapes fall out without being special-cased. A NaN desire
            // reports nothing here: that is ValidateLayout's fault to name, not this one's.
            var overWidth = desired.x - constraints.MaxWidth;
            if ((considered & LayoutAxes.Horizontal) != 0
                && float.IsFinite(desired.x)
                && overWidth > LayoutConstants.OverflowTolerance)
            {
                axes |= LayoutAxes.Horizontal;
                amount = Mathf.Max(amount, overWidth);
            }

            var overHeight = desired.y - constraints.MaxHeight;
            if ((considered & LayoutAxes.Vertical) != 0
                && float.IsFinite(desired.y)
                && overHeight > LayoutConstants.OverflowTolerance)
            {
                axes |= LayoutAxes.Vertical;
                amount = Mathf.Max(amount, overHeight);
            }

            if (axes == LayoutAxes.None)
            {
                return;
            }

            // No size, for the same reason ReportOverflow passes none: this fires from inside
            // PerformSizing, where Size still holds the previous pass's answer. The number that matters
            // is the shortfall, and it is in Amount.
            Emit(
                new LayoutIssue(
                    LayoutIssueCode.ContentOverflow,
                    this.Owner,
                    axes,
                    remedy,
                    constraints: constraints,
                    amount: amount
                )
            );
#endif
        }

        /// <summary>
        ///     Whether a child drawn outside this render object's box is intended rather than a fault.
        /// </summary>
        /// <remarks>
        ///     True for the render objects whose whole purpose involves painting outside themselves: a
        ///     scrollable, whose children are positioned beyond the viewport by definition; a stack with
        ///     positioned children; anything that anchors a box to something else. False everywhere
        ///     else, because a child outside its parent is drawn there -- this layer does not clip -- and
        ///     nothing else about the pass will say so.
        /// </remarks>
        protected virtual bool ChildrenMayOverhang => false;

        /// <summary>A child ended up positioned outside this render object's box.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNIMOB_UI_FORCE_DIAGNOSTICS")]
        protected void ReportChildOutOfBounds(
            int childIndex,
            LayoutAxes axes,
            float amount,
            string remedy
        )
        {
#if UNIMOB_UI_DIAGNOSTICS
            MarkCulprit(childIndex, LayoutIssueCode.ChildOutOfBounds);

            Emit(
                new LayoutIssue(
                    LayoutIssueCode.ChildOutOfBounds,
                    this.Owner,
                    axes,
                    remedy,
                    ChildAt(childIndex),
                    _constraints.Value,
                    size: _size,
                    amount: amount
                )
            );
#endif
        }

        /// <summary>
        ///     How far <paramref name="childPosition"/>/<paramref name="childSize"/> sticks out of a box
        ///     of <paramref name="size"/>, per axis, and by how much at worst.
        /// </summary>
        /// <remarks>
        ///     Positions are top-left origin with y increasing downwards, so a child occupies
        ///     [x, x + w] by [y, y + h] inside [0, width] by [0, height]. Overhang is counted on both
        ///     edges: a centred child that does not fit escapes at the near edge as well as the far one,
        ///     which is precisely the case that looks like "the content is shifted down".
        /// </remarks>
        private protected static LayoutAxes Overhang(
            Vector2 childPosition,
            Vector2 childSize,
            Vector2 size,
            out float amount
        )
        {
            amount = 0f;
            var axes = LayoutAxes.None;

            var horizontal = Mathf.Max(
                -childPosition.x,
                childPosition.x + childSize.x - size.x
            );
            if (float.IsFinite(horizontal) && horizontal > LayoutConstants.OverflowTolerance)
            {
                axes |= LayoutAxes.Horizontal;
                amount = Mathf.Max(amount, horizontal);
            }

            var vertical = Mathf.Max(-childPosition.y, childPosition.y + childSize.y - size.y);
            if (float.IsFinite(vertical) && vertical > LayoutConstants.OverflowTolerance)
            {
                axes |= LayoutAxes.Vertical;
                amount = Mathf.Max(amount, vertical);
            }

            return axes;
        }

        private protected const string SomethingHereIsBiggerThanItsBox =
            "This child is drawn outside its parent, which does not clip. Give the parent more room, "
            + "make the child smaller, or let the content scroll.";

        /// <summary>An axis reached something that cannot work without a bound.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNIMOB_UI_FORCE_DIAGNOSTICS")]
        protected void ReportUnboundedConstraint(
            LayoutAxes axes,
            LayoutConstraints constraints,
            string remedy
        )
        {
#if UNIMOB_UI_DIAGNOSTICS
            Emit(
                new LayoutIssue(
                    LayoutIssueCode.UnboundedConstraint,
                    this.Owner,
                    axes,
                    remedy,
                    constraints: constraints
                )
            );
#endif
        }

        /// <summary>A child answered with infinity or NaN.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNIMOB_UI_FORCE_DIAGNOSTICS")]
        protected void ReportNonFiniteChildSize(int childIndex, LayoutAxes axes, string remedy)
        {
#if UNIMOB_UI_DIAGNOSTICS
            MarkCulprit(childIndex, LayoutIssueCode.NonFiniteChildSize);

            Emit(
                new LayoutIssue(
                    LayoutIssueCode.NonFiniteChildSize,
                    this.Owner,
                    axes,
                    remedy,
                    ChildAt(childIndex),
                    _constraints.Value
                )
            );
#endif
        }

        /// <summary>This render object answered with infinity or NaN under a finite maximum.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNIMOB_UI_FORCE_DIAGNOSTICS")]
        protected void ReportNonFiniteSize(LayoutAxes axes, LayoutConstraints constraints, string remedy)
        {
#if UNIMOB_UI_DIAGNOSTICS
            Emit(
                new LayoutIssue(
                    LayoutIssueCode.NonFiniteSize,
                    this.Owner,
                    axes,
                    remedy,
                    constraints: constraints,
                    size: _size
                )
            );
#endif
        }

        /// <summary>
        ///     A checked postcondition, run at the end of every pass: <b>an infinite size out is legal
        ///     exactly when the constraint in was infinite on that axis.</b>
        /// </summary>
        /// <remarks>
        ///     Passing infinity along under infinity is passing the buck upward, and stays legal right
        ///     up until it reaches a RectTransform. Returning it under a finite maximum is always this
        ///     render object's own fault, and there is nowhere further up for it to be caught.
        ///     <para>
        ///         Report only, never repair. Five render objects already materialise an unbounded axis
        ///         by hand, each to a value only its own algorithm knows; a base class clamping on their
        ///         behalf would be guessing.
        ///     </para>
        ///     <para>
        ///         Not <c>[Conditional]</c>: that attribute is invalid on an override (CS0243), so the
        ///         hook cannot carry it and its single call site does instead.
        ///     </para>
        /// </remarks>
        protected virtual void ValidateLayout(LayoutConstraints constraints)
        {
            var axes = LayoutAxes.None;

            if (!float.IsFinite(_size.x) && float.IsFinite(constraints.MaxWidth))
            {
                axes |= LayoutAxes.Horizontal;
            }

            if (!float.IsFinite(_size.y) && float.IsFinite(constraints.MaxHeight))
            {
                axes |= LayoutAxes.Vertical;
            }

            if (axes != LayoutAxes.None)
            {
                ReportNonFiniteSize(axes, constraints, MaterialiseAgainstTheMaximum);
                return;
            }

            ValidateAgainstTheMaximum(constraints);
        }

        /// <summary>
        ///     The contract every push depends on: <b>a render object answers with a size its own
        ///     constraints allow.</b>
        /// </summary>
        /// <remarks>
        ///     Flutter enforces exactly this in the <c>RenderBox.size</c> setter, where a violation is
        ///     fatal in a debug build. This layer inherited the protocol without the check, and a good
        ///     deal of code assumes it holds: <see cref="RenderConstrainedBox"/> returns its child's size
        ///     verbatim, and several render objects skip constraining a result they know came from a
        ///     child laid out under a maximum they chose. Each of those is correct only while this is
        ///     true, and nothing was verifying it.
        ///     <para>
        ///         Checked on the answering render object rather than in its parent's LayoutChild, so
        ///         the widget that broke the promise is the one named, the one latched, and the one the
        ///         stripe is drawn over. Reported through the parent would blame whichever widget did
        ///         the asking and would let one violating sibling suppress the next.
        ///     </para>
        ///     <para>
        ///         Reported, not thrown. This has never been enforced, so a first run may well find
        ///         standing violations, and halting on them is a worse trade than naming them.
        ///     </para>
        ///     <para>
        ///         The maximum only. Under-filling a tight minimum is a different fault with a different
        ///         remedy and puts no pixels outside a box, and a non-finite answer is already
        ///         <see cref="LayoutIssueCode.NonFiniteSize"/> -- which is why that case returns above
        ///         rather than being named twice.
        ///     </para>
        /// </remarks>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNIMOB_UI_FORCE_DIAGNOSTICS")]
        private void ValidateAgainstTheMaximum(LayoutConstraints constraints)
        {
#if UNIMOB_UI_DIAGNOSTICS
            var axes = LayoutAxes.None;
            var amount = 0f;

            var overWidth = _size.x - constraints.MaxWidth;
            if (overWidth > LayoutConstants.OverflowTolerance)
            {
                axes |= LayoutAxes.Horizontal;
                amount = Mathf.Max(amount, overWidth);
            }

            var overHeight = _size.y - constraints.MaxHeight;
            if (overHeight > LayoutConstants.OverflowTolerance)
            {
                axes |= LayoutAxes.Vertical;
                amount = Mathf.Max(amount, overHeight);
            }

            if (axes == LayoutAxes.None)
            {
                return;
            }

            Emit(
                new LayoutIssue(
                    LayoutIssueCode.SizeExceedsConstraints,
                    this.Owner,
                    axes,
                    ConstrainTheResult,
                    constraints: constraints,
                    size: _size,
                    amount: amount
                )
            );
#endif
        }

        private const string ConstrainTheResult =
            "A render object may only answer with a size its constraints allow. Pass the result "
            + "through LayoutConstraints.Constrain before returning it.";

        private const string MaterialiseAgainstTheMaximum =
            "A render object may answer with an infinite axis only where the constraint it was given "
            + "was infinite on that axis. Constrain the result before returning it.";

        /// <summary>
        ///     The child at <paramref name="index"/>, for a render object with ordered children.
        /// </summary>
        [CanBeNull]
        protected virtual IState ChildAt(int index) => null;

        /// <summary>
        ///     Records that a specific child is implicated, for the in-scene marker.
        /// </summary>
        /// <remarks>
        ///     Separate from the report because the two have opposite triggers: the console says a fault
        ///     started, the marker says it is happening. Done by the facade rather than the algorithm,
        ///     so no layout method has to know the overlay exists.
        /// </remarks>
        protected virtual void MarkCulprit(int index, LayoutIssueCode code) { }

#if UNIMOB_UI_DIAGNOSTICS
        private void Emit(in LayoutIssue issue)
        {
            var bit = 1 << (int) issue.Code;

            // Always, even when suppressed. Noticing is not emitting: without this, a fault that
            // persists clears the latch at the end of every pass and reports on every other one --
            // which is the flood, at half rate, plus a level signal that blinks.
            _seenThisPass |= bit;

            if ((_reported & bit) != 0)
            {
                return;
            }

            _reported |= bit;

            using (Atom.NoWatch)
            {
                UniMobDiagnostics.Report(issue);
            }
        }
#endif
    }
}
