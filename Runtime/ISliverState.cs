using System.Collections.Generic;
using UniMob.UI.Rendering;

namespace UniMob.UI
{
    public interface ISliverState : IMultiChildLayoutState
    {
        /// <summary>Eager mode only (<see cref="ItemCount"/> is null); empty otherwise.</summary>
        IState[] AllChildren { get; }

        /// <summary>Lazy mode only; the full logical item count. Null in eager mode.</summary>
        int? ItemCount { get; }

        /// <summary>Lazy mode only; when set, every item has exactly this extent (no measuring/estimating needed).</summary>
        float? ItemExtent { get; }

        Axis Axis { get; }

        /// <summary>
        ///     Current scroll offset in pixels along the scrolling axis. An absolute value, not a 0..1 ratio --
        ///     see <see cref="UniMob.UI.ScrollController.PixelOffset"/> for why this render object needs that.
        /// </summary>
        float ScrollPixelOffset { get; }
        float? VirtualizationCacheExtent { get; }
        float Spacing { get; }

        void SetVisibleChildren(List<IndexedLayoutData> visibleChildren);

        /// <summary>
        ///     Lazy mode only. Ensures indices in [startIndexInclusive, endIndexExclusive) are built (constructing
        ///     newly-entering ones and evicting ones that fell outside the window since the last call), and returns
        ///     their states in index order.
        /// </summary>
        IState[] RequestBuildWindow(int startIndexInclusive, int endIndexExclusive);
    }
}
