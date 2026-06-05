using System;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    /// <summary>
    /// A base class for render objects that have no children (e.g., Text, Image, TextInput).
    /// </summary>
    public abstract class LeafRenderObject : RenderObject
    {
        protected LeafRenderObject(Lifetime lifetime) : base(lifetime) { }
        
        protected sealed override void PerformPositioning()
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
}