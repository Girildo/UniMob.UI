using UnityEngine;

namespace UniMob.UI
{
    /// <summary>
    ///     The geometry of an attached scrollable along its scroll axis. <see cref="PixelOffset" /> is not
    ///     clamped: an Elastic overscroll puts it below 0 or above <see cref="MaxScrollExtent" />. For a
    ///     lazy list <see cref="ContentExtent" /> is an estimate that changes between layout passes.
    /// </summary>
    public readonly record struct ScrollMetrics(
        float PixelOffset,
        float ContentExtent,
        float ViewportExtent,
        Axis Axis
    )
    {
        public float MaxScrollExtent => Mathf.Max(0, ContentExtent - ViewportExtent);

        public bool CanScroll => MaxScrollExtent > 0;
    }
}
