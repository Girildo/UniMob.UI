using UniMob.UI.Rendering;

namespace UniMob.UI
{
    /// <summary>
    /// A state whose child is placed against the edges of the stack that holds it, instead of being
    /// aligned within it.
    /// </summary>
    /// <remarks>
    /// A stack takes its size from its non-positioned children alone, then places each positioned child
    /// against that size. An axis is pinned by an explicit extent, or by both of its edges, which
    /// together state one; an axis with neither is laid out unbounded, so the child takes its own size
    /// on it and may sit outside the stack. At most two of <see cref="Left"/>, <see cref="Right"/> and
    /// <see cref="Width"/> may be answered, and likewise for <see cref="Top"/>, <see cref="Bottom"/> and
    /// <see cref="Height"/>; a third over-constrains the axis.
    /// </remarks>
    public interface IPositionedState : ISingleChildLayoutState
    {
        float? Left { get; }
        float? Top { get; }
        float? Right { get; }
        float? Bottom { get; }
        float? Width { get; }
        float? Height { get; }
    }
}
