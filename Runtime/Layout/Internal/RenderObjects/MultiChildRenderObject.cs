using System.Collections.Generic;
using UniMob.UI.Diagnostics;
using UniMob.UI.Layout.Internal.Views;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    /// <summary>
    ///     Base for a render object that lays out an ordered set of children and reports where each
    ///     one landed.
    /// </summary>
    /// <remarks>
    ///     Exists to own <see cref="ChildrenLayout"/> rather than to share any layout algorithm --
    ///     the subclasses have nothing else in common. The getter has to pull layout before handing
    ///     the buffer out, because the buffer is only valid immediately after a pass, and a caller
    ///     that reads it without one stamps last frame's positions onto live RectTransforms. That
    ///     failure is silent and intermittent: it shows only while something else happens to be
    ///     moving.
    ///     <para>
    ///         So the getter is sealed and the buffer is what subclasses write. Stated as a rule on
    ///         the interface it was seven identical copies, one per implementor, and the eighth
    ///         implementor would have been the one to forget.
    ///     </para>
    /// </remarks>
    public abstract class MultiChildRenderObject : RenderObject, IMultiChildrenRenderObject
    {
        protected MultiChildRenderObject(IState owner)
            : base(owner) { }

        /// <summary>
        ///     Where subclasses record each child's size and position during their layout pass.
        ///     Written during a pass; read only through <see cref="ChildrenLayout"/>.
        /// </summary>
        protected readonly List<LayoutInfo> ChildrenLayoutBuffer = new List<LayoutInfo>();

        /// <inheritdoc/>
        public IReadOnlyList<LayoutInfo> ChildrenLayout
        {
            get
            {
                WatchLayout();
                return ChildrenLayoutBuffer;
            }
        }

        /// <inheritdoc/>
        protected override IState ChildAt(int index)
        {
            if (this.Owner is not IMultiChildLayoutState state)
            {
                return null;
            }

            var children = state.Children;
            return index >= 0 && index < children.Length ? children[index] : null;
        }

        /// <inheritdoc/>
        protected override void MarkCulprit(int index, LayoutIssueCode code)
        {
#if UNITY_EDITOR
            if (index < 0 || index >= ChildrenLayoutBuffer.Count)
            {
                return;
            }

            var layout = ChildrenLayoutBuffer[index];
            layout.DebugWarning = code.ToString();
            ChildrenLayoutBuffer[index] = layout;
#endif
        }
    }
}
