using System;
using UnityEngine;

namespace UniMob.UI.Layout
{
    // ================================================================================================
    //  Grid geometry seam -- the reusable core that lets a ScrollGrid virtualize by row.
    // ================================================================================================
    //
    //  Modelled on Flutter's SliverGridDelegate -> SliverGridLayout. A delegate turns the viewport's
    //  available cross-axis extent into a regular tile layout: how many columns, how wide each cell is,
    //  and (optionally) how tall. Everything here is a pure function of the cross-axis extent and the
    //  item index -- no child is ever measured to decide the grid shape, which is exactly what makes a
    //  uniform grid lazily virtualizable: item i is deterministically at (row i/N, column i%N).
    //
    //  What lives here vs. in the render object:
    //    * The delegate owns the DETERMINISTIC decision (column count, cell cross-extent, and -- when the
    //      cell's scroll-axis size is fixed -- its main-axis extent + strides).
    //    * When CellMainAxisExtent is null the grid is in "measured rows" mode: each row is as tall as its
    //      tallest child, so the main-axis offset model is inherently measurement-driven and lives in
    //      RenderSliverGrid (mirroring RenderSliverList's measured-extent + EMA model), not here.
    //
    //  A future masonry layout would be a different delegate whose result the render object interprets
    //  sequentially (per-column packing, forward-only) -- it simply wouldn't be a regular tile layout.
    // ================================================================================================

    /// <summary>
    ///     The resolved, regular-tile grid geometry for one layout pass, produced by a
    ///     <see cref="SliverGridDelegate" /> from the viewport's available cross-axis extent.
    /// </summary>
    public readonly struct SliverGridLayout
    {
        public SliverGridLayout(
            int crossAxisCount,
            float cellCrossAxisExtent,
            float crossAxisSpacing,
            float mainAxisSpacing,
            float? cellMainAxisExtent
        )
        {
            CrossAxisCount = crossAxisCount;
            CellCrossAxisExtent = cellCrossAxisExtent;
            CrossAxisSpacing = crossAxisSpacing;
            MainAxisSpacing = mainAxisSpacing;
            CellMainAxisExtent = cellMainAxisExtent;
        }

        /// <summary>Number of cells laid across the cross axis (columns for a vertical grid).</summary>
        public int CrossAxisCount { get; }

        /// <summary>Size of each cell along the cross axis.</summary>
        public float CellCrossAxisExtent { get; }

        /// <summary>Gap between cells along the cross axis.</summary>
        public float CrossAxisSpacing { get; }

        /// <summary>Gap between rows along the main (scroll) axis.</summary>
        public float MainAxisSpacing { get; }

        /// <summary>
        ///     Fixed size of each cell along the main (scroll) axis, or <c>null</c> when rows are measured
        ///     (each row is as tall as its tallest child, resolved by the render object).
        /// </summary>
        public float? CellMainAxisExtent { get; }

        /// <summary><c>true</c> when every cell has the same known main-axis extent (no measuring needed).</summary>
        public bool IsFixedMainAxis => CellMainAxisExtent.HasValue;

        /// <summary>Distance from one cell's leading cross edge to the next column's.</summary>
        public float CrossAxisStride => CellCrossAxisExtent + CrossAxisSpacing;

        public int RowOf(int index) => index / CrossAxisCount;

        public int ColumnOf(int index) => index % CrossAxisCount;

        public int RowCount(int itemCount) =>
            itemCount <= 0 ? 0 : (itemCount + CrossAxisCount - 1) / CrossAxisCount;

        /// <summary>
        ///     Leading cross-axis edge of <paramref name="column" />, relative to the content's cross-axis
        ///     origin. The caller adds the cross-axis start padding.
        /// </summary>
        public float CrossAxisOffsetForColumn(int column) => column * CrossAxisStride;
    }

    /// <summary>
    ///     Decides the cross-axis grid geometry for a <see cref="ScrollGrid" />. Given the viewport's
    ///     available cross-axis extent (already net of cross-axis padding), returns how a row is divided
    ///     into columns and, optionally, the fixed cell size along the scroll axis. Mirrors Flutter's
    ///     <c>SliverGridDelegate</c>.
    /// </summary>
    public abstract class SliverGridDelegate
    {
        public abstract SliverGridLayout GetLayout(float availableCrossAxisExtent);

        // childAspectRatio is defined (as in Flutter) as crossAxisExtent / mainAxisExtent, so a ratio > 1
        // is wider-than-tall and < 1 is taller-than-wide. MainAxisExtent, when given, wins outright.
        private protected static float? ResolveCellMainExtent(
            float? mainAxisExtent,
            float? childAspectRatio,
            float cellCrossExtent
        )
        {
            if (mainAxisExtent.HasValue)
                return mainAxisExtent.Value;
            if (childAspectRatio.HasValue)
                return cellCrossExtent / childAspectRatio.Value;
            return null;
        }
    }

    /// <summary>
    ///     A grid delegate with a fixed number of columns. Each cell's cross-axis extent is the available
    ///     extent split evenly across <see cref="CrossAxisCount" /> (minus spacing). The cell's main-axis
    ///     extent is <see cref="MainAxisExtent" /> if set, else derived from <see cref="ChildAspectRatio" />
    ///     (cross / main), else <c>null</c> -> the rows are measured.
    /// </summary>
    public sealed class SliverGridDelegateWithFixedCrossAxisCount : SliverGridDelegate
    {
        public SliverGridDelegateWithFixedCrossAxisCount(
            int crossAxisCount,
            float mainAxisSpacing = 0f,
            float crossAxisSpacing = 0f,
            float? childAspectRatio = null,
            float? mainAxisExtent = null
        )
        {
            if (crossAxisCount < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(crossAxisCount),
                    "CrossAxisCount must be >= 1."
                );

            CrossAxisCount = crossAxisCount;
            MainAxisSpacing = mainAxisSpacing;
            CrossAxisSpacing = crossAxisSpacing;
            ChildAspectRatio = childAspectRatio;
            MainAxisExtent = mainAxisExtent;
        }

        public int CrossAxisCount { get; }
        public float MainAxisSpacing { get; }
        public float CrossAxisSpacing { get; }
        public float? ChildAspectRatio { get; }
        public float? MainAxisExtent { get; }

        public override SliverGridLayout GetLayout(float availableCrossAxisExtent)
        {
            var usable = Mathf.Max(
                0f,
                availableCrossAxisExtent - CrossAxisSpacing * (CrossAxisCount - 1)
            );
            var cellCross = usable / CrossAxisCount;
            var cellMain = ResolveCellMainExtent(MainAxisExtent, ChildAspectRatio, cellCross);
            return new SliverGridLayout(
                CrossAxisCount,
                cellCross,
                CrossAxisSpacing,
                MainAxisSpacing,
                cellMain
            );
        }
    }

    /// <summary>
    ///     A grid delegate that fits as many columns as possible so each cell's cross-axis extent is at
    ///     most <see cref="MaxCrossAxisExtent" />. The column count adapts to the viewport, making the grid
    ///     responsive across screen sizes. Main-axis sizing matches
    ///     <see cref="SliverGridDelegateWithFixedCrossAxisCount" />.
    /// </summary>
    public sealed class SliverGridDelegateWithMaxCrossAxisExtent : SliverGridDelegate
    {
        public SliverGridDelegateWithMaxCrossAxisExtent(
            float maxCrossAxisExtent,
            float mainAxisSpacing = 0f,
            float crossAxisSpacing = 0f,
            float? childAspectRatio = null,
            float? mainAxisExtent = null
        )
        {
            if (maxCrossAxisExtent <= 0f)
                throw new ArgumentOutOfRangeException(
                    nameof(maxCrossAxisExtent),
                    "MaxCrossAxisExtent must be > 0."
                );

            MaxCrossAxisExtent = maxCrossAxisExtent;
            MainAxisSpacing = mainAxisSpacing;
            CrossAxisSpacing = crossAxisSpacing;
            ChildAspectRatio = childAspectRatio;
            MainAxisExtent = mainAxisExtent;
        }

        public float MaxCrossAxisExtent { get; }
        public float MainAxisSpacing { get; }
        public float CrossAxisSpacing { get; }
        public float? ChildAspectRatio { get; }
        public float? MainAxisExtent { get; }

        public override SliverGridLayout GetLayout(float availableCrossAxisExtent)
        {
            // As in Flutter: pick the fewest columns such that each cell (plus one inter-cell gap) fits
            // under the max. Clamp to at least one column so a viewport narrower than the max still works.
            var crossAxisCount = Mathf.Max(
                1,
                Mathf.CeilToInt(availableCrossAxisExtent / (MaxCrossAxisExtent + CrossAxisSpacing))
            );
            var usable = Mathf.Max(
                0f,
                availableCrossAxisExtent - CrossAxisSpacing * (crossAxisCount - 1)
            );
            var cellCross = usable / crossAxisCount;
            var cellMain = ResolveCellMainExtent(MainAxisExtent, ChildAspectRatio, cellCross);
            return new SliverGridLayout(
                crossAxisCount,
                cellCross,
                CrossAxisSpacing,
                MainAxisSpacing,
                cellMain
            );
        }
    }
}
