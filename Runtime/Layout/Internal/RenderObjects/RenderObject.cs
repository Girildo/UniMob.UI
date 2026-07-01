using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    /// <summary>
    ///     A pure C# object that handles all layout calculation for a LayoutWidget.
    ///     It is decoupled from MonoBehaviour and the Unity rendering pipeline.
    /// </summary>
    public abstract class RenderObject
    {
        protected Lifetime Lifetime { get; }

        protected RenderObject(Lifetime lifetime)
        {
            this.Lifetime = lifetime;
        }

        public Vector2 Size { get; private set; } // The final size after layout

        /// <summary>
        ///     Performs the layout calculation for this widget and its children.
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
        /// </remarks>
        public void PerformLayoutImmediate(LayoutConstraints constraints)
        {
            if(this.Lifetime.IsDisposed)
                return;
            // Phase 1: Perform this widget's own size.
            Size = PerformSizing(constraints);

            // Phase 2: Perform layout for children.
            PerformPositioning();
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
            if(this.Lifetime.IsDisposed)
                return Vector2.zero;
            if (child is null)
                return Vector2.zero;
            

            child.UpdateConstraints(constraints);

            // WatchedSize (not WatchedPerformLayout) deliberately: we only care about the child's
            // resulting Size here, not about being re-run whenever the child's subtree merely
            // repositions itself internally (e.g. a nested ScrollList scrolling). See IState.WatchedSize.
            var childSize = child.WatchedSize();

            return childSize;
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