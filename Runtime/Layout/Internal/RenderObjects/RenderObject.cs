using UnityEngine;

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

        protected Lifetime Lifetime { get; }

        protected RenderObject(Lifetime lifetime)
        {
            this.Lifetime = lifetime;

            _trackedLayout = Atom.Computed(lifetime, PerformLayout);
            _trackedSize = Atom.Computed(lifetime, () => _trackedLayout.Get().size);
        }

        /// <summary>The final size after layout.</summary>
        public Vector2 Size { get; private set; }

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
        ///             <c>RenderObject.Size</c> (Vector2): The final, calculated width and height of the widget.
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
                return (Size, _layoutVersion);
            }

            var constraints = _constraints.Value;
            if (!constraints.HasValue)
            {
                // Never laid out. Nothing can be computed yet, and inventing a value here would be
                // indistinguishable from having been laid out at that size.
                return (Size, _layoutVersion);
            }

            // Phase 1: Perform this widget's own size.
            Size = PerformSizing(constraints.Value);

            // Phase 2: Perform layout for children.
            PerformPositioning();

            // A pass ran, and subscribers to WatchLayout must re-run even if the size is unchanged,
            // so the version always moves.
            return (Size, _layoutVersion = (_layoutVersion + 1) % int.MaxValue);
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
        ///     This method is called after the size has been determined to position the widget.
        ///     Subclasses should implement this to set the position of the widget, accessing, if necessary,
        ///     the <see cref="Size"/> field that has been calculated in the sizing phase.
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
        protected abstract void PerformPositioning();

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
    }
}
