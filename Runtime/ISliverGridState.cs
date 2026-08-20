using System.Collections.Generic;
using UniMob.UI.Rendering;

namespace UniMob.UI
{
    public interface ISliverGridState : IMultiChildLayoutState
    {
        /// <summary>Eager mode only (<see cref="ItemCount" /> is null); empty otherwise.</summary>
        IState[] AllChildren { get; }

        /// <summary>Lazy mode only; the full logical item count. Null in eager mode.</summary>
        int? ItemCount { get; }

        Axis Axis { get; }

        /// <summary>Current absolute scroll offset in pixels along the scrolling axis.</summary>
        float ScrollPixelOffset { get; }

        float? VirtualizationCacheExtent { get; }

        /// <summary>Decides the cross-axis geometry (column count, cell size) each layout pass.</summary>
        SliverGridDelegate GridDelegate { get; }

        /// <summary>Padding around the grid content, in the widget's own x/y space.</summary>
        RectPadding Padding { get; }

        void SetVisibleChildren(List<IndexedLayoutData> visibleChildren);

        /// <summary>
        ///     Lazy mode only. Ensures indices in [startIndexInclusive, endIndexExclusive) are built and
        ///     returns their states in index order. Same contract as <see cref="ISliverState.RequestBuildWindow" />.
        /// </summary>
        IState[] RequestBuildWindow(int startIndexInclusive, int endIndexExclusive);
    }
}
