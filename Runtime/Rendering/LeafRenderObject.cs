using System;
using UnityEngine;

namespace UniMob.UI.Rendering
{
    /// <summary>
    /// A base class for render objects that have no children (e.g., Text, Image, TextInput).
    /// </summary>
    public abstract class LeafRenderObject : RenderObject
    {
        protected LeafRenderObject(IState owner)
            : base(owner) { }

        protected sealed override void PerformPositioning(Vector2 size)
        {
            // Nothing to position inside a leaf.
        }

        /// <summary>
        ///     A helper method to handle the boilerplate of laying out a child.
        ///     It performs the layout and returns the child's final calculated size.
        /// </summary>
        /// <param name="child">The child state to layout.</param>
        /// <param name="constraints">The constraints to apply to the child.</param>
        /// <returns>The final size of the child after layout.</returns>
        protected new Vector2 LayoutChild(IState child, LayoutConstraints constraints)
        {
            throw new InvalidOperationException("Leaf nodes cannot layout children.");
        }
    }

    // Remark: this is, conceptually, something we could implement using a RenderConstrainedBox with
    // no child.
    // However, to support the reactivity model of UniMob, we can't use primitives and we need to rely
    // on either interfaces with explicit Child getters or pull delegates.
    // To avoid overcomplicating the RenderConstrainedBox with pull delegates,
    // we implement this simple RenderConstrainedLeaf that is just a leaf version of the ConstrainedBox,
    // making it flexible enough to be used in a wide variety of scenarios where we want a leaf with dynamic constraints.

    /// <summary>
    /// A render object that represents a leaf node with dynamic constraints.
    /// It tries sizing itself to the smallest size allowed by its constraints, which are pulled from the state via a delegate.
    /// </summary>
    public sealed class RenderConstrainedLeaf : LeafRenderObject
    {
        private Func<LayoutConstraints> pullConstraints;

        /// <summary>
        /// Initializes a new instance of the <see cref="RenderConstrainedLeaf"/> class with the specified state and <b>static</b> constraints.
        /// </summary>
        /// <remarks>
        /// This constructor is a convenience overload for cases where the constraints are not expected to change over the lifetime of the render object.
        /// If the constraints need to be dynamic, consider using the constructor that accepts a <see cref="Func{LayoutConstraints}"/> delegate to pull the constraints from the state.
        /// </remarks>
        public RenderConstrainedLeaf(IState state, LayoutConstraints constraints)
            : this(state, () => constraints) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="RenderConstrainedLeaf"/> class with the specified state and a delegate to pull dynamic constraints.
        /// </summary>
        public RenderConstrainedLeaf(IState state, Func<LayoutConstraints> pullConstraints)
            : base(state)
        {
            this.pullConstraints = pullConstraints;
        }

        protected override float ComputeIntrinsicWidth(float height)
        {
            var width = pullConstraints().MinWidth;
            return float.IsPositiveInfinity(width) ? 0f : width;
        }

        protected override float ComputeIntrinsicHeight(float width)
        {
            var height = pullConstraints().MinHeight;
            return float.IsPositiveInfinity(height) ? 0f : height;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            // Get our own constraints, by asking the caller to pull them from our state.
            // This allows our constraints to be dynamic and change over time.
            // If pull constraints read any Atom this triggers automatically the re-layout.
            var selfConstraints = pullConstraints();

            // Merge our requested rules with the parent's strict rules.
            // (e.g., If we ask to be 500px wide, but parent says MaxWidth is 100px,
            // Enforce() will correctly clamp our rule to 100px).
            var effectiveConstraints = selfConstraints.Enforce(constraints);

            // A leaf with no children always tries to be as small as legally possible.
            // If the requested constraints dictate "Expand", MinWidth will equal the parent's MaxWidth!
            float width = effectiveConstraints.MinWidth;
            float height = effectiveConstraints.MinHeight;

            // Handle infinity:
            // If we are asked to expand in an infinitely scrollable axis, we safely collapse
            // back to the parent's minimum size (usually 0) to prevent a Unity RectTransform crash.
            if (float.IsPositiveInfinity(width))
                width = constraints.MinWidth;
            if (float.IsPositiveInfinity(height))
                height = constraints.MinHeight;

            return new Vector2(width, height);
        }
    }
}
