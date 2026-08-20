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
        ///     Reading this both runs layout if needed and subscribes to it, so the positions cannot
        ///     be stale by construction. A plain list would be valid only immediately after a pull the
        ///     caller has to remember to make separately, and forgetting it writes last frame's
        ///     positions onto live RectTransforms with nothing to catch it.
        /// </remarks>
        IReadOnlyList<LayoutInfo> ChildrenLayout { get; }
    }
}
