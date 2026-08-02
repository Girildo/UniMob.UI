using System.Collections.Generic;


namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public interface IMultiChildrenRenderObject
    {
        /// <summary>
        ///     <b>[Atom]</b> Per-child size and position, as of a layout pass this getter performs
        ///     before returning.
        /// </summary>
        /// <remarks>
        ///     Reading this both runs layout if needed and subscribes to it, so the positions cannot be
        ///     stale by construction. It used to be a plain list valid only immediately after a pull the
        ///     caller had to remember to make separately; forgetting it stamped last frame's positions
        ///     onto live RectTransforms, with nothing to catch it.
        /// </remarks>
        IReadOnlyList<LayoutInfo> ChildrenLayout { get; }
    }
}