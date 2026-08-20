using System;
using NUnit.Framework;
using UniMob.UI.Layout;

namespace UniMob.UI.Tests
{
    // Pure geometry: a SliverGridDelegate turns an available cross-axis extent into a regular tile
    // layout with no children involved. These lock down the column-count / cell-size arithmetic and
    // the deterministic index -> (row, column) mapping the whole grid virtualization relies on.
    public class SliverGridDelegateTests
    {
        [Test]
        public void FixedCount_SplitsCrossExtentEvenly()
        {
            var layout = new SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount: 3).GetLayout(
                300f
            );

            Assert.AreEqual(3, layout.CrossAxisCount);
            Assert.AreEqual(100f, layout.CellCrossAxisExtent, 0.01f);
            Assert.AreEqual(100f, layout.CrossAxisStride, 0.01f);
            Assert.AreEqual(0f, layout.CrossAxisOffsetForColumn(0), 0.01f);
            Assert.AreEqual(100f, layout.CrossAxisOffsetForColumn(1), 0.01f);
            Assert.AreEqual(200f, layout.CrossAxisOffsetForColumn(2), 0.01f);
        }

        [Test]
        public void FixedCount_AccountsForCrossAxisSpacing()
        {
            // 3 columns, 2 inter-column gaps of 5 => usable 300 => 100 per cell, stride 105.
            var layout = new SliverGridDelegateWithFixedCrossAxisCount(
                3,
                crossAxisSpacing: 5f
            ).GetLayout(310f);

            Assert.AreEqual(100f, layout.CellCrossAxisExtent, 0.01f);
            Assert.AreEqual(105f, layout.CrossAxisStride, 0.01f);
            Assert.AreEqual(210f, layout.CrossAxisOffsetForColumn(2), 0.01f);
        }

        [Test]
        public void ChildAspectRatio_DerivesMainExtent()
        {
            // ratio = cross / main. avail 200 / 2 cols => cellCross 100. ratio 0.5 => main = 100 / 0.5 = 200.
            var layout = new SliverGridDelegateWithFixedCrossAxisCount(
                2,
                childAspectRatio: 0.5f
            ).GetLayout(200f);

            Assert.IsTrue(layout.IsFixedMainAxis);
            Assert.AreEqual(100f, layout.CellCrossAxisExtent, 0.01f);
            Assert.AreEqual(200f, layout.CellMainAxisExtent.Value, 0.01f);
        }

        [Test]
        public void MainAxisExtent_OverridesAspectRatio()
        {
            var layout = new SliverGridDelegateWithFixedCrossAxisCount(
                2,
                childAspectRatio: 1f,
                mainAxisExtent: 50f
            ).GetLayout(200f);

            Assert.IsTrue(layout.IsFixedMainAxis);
            Assert.AreEqual(50f, layout.CellMainAxisExtent.Value, 0.01f);
        }

        [Test]
        public void NoMainSizing_IsMeasuredRows()
        {
            var layout = new SliverGridDelegateWithFixedCrossAxisCount(2).GetLayout(200f);

            Assert.IsFalse(layout.IsFixedMainAxis);
            Assert.IsNull(layout.CellMainAxisExtent);
        }

        [Test]
        public void MaxExtent_PicksFewestColumnsUnderTheCap()
        {
            var del = new SliverGridDelegateWithMaxCrossAxisExtent(maxCrossAxisExtent: 200f);

            // 600 / 200 = 3 exactly.
            Assert.AreEqual(3, del.GetLayout(600f).CrossAxisCount);
            Assert.AreEqual(200f, del.GetLayout(600f).CellCrossAxisExtent, 0.01f);

            // 650 / 200 = 3.25 -> 4 columns, each <= 200.
            var wide = del.GetLayout(650f);
            Assert.AreEqual(4, wide.CrossAxisCount);
            Assert.AreEqual(162.5f, wide.CellCrossAxisExtent, 0.01f);
        }

        [Test]
        public void MaxExtent_ClampsToAtLeastOneColumn()
        {
            var del = new SliverGridDelegateWithMaxCrossAxisExtent(200f);

            var narrow = del.GetLayout(100f); // viewport narrower than the cap
            Assert.AreEqual(1, narrow.CrossAxisCount);
            Assert.AreEqual(100f, narrow.CellCrossAxisExtent, 0.01f);
        }

        [Test]
        public void MaxExtent_AccountsForCrossAxisSpacing()
        {
            // ceil(650 / (200 + 10)) = ceil(3.095) = 4 columns; usable = 650 - 3*10 = 620 => 155 each.
            var layout = new SliverGridDelegateWithMaxCrossAxisExtent(
                200f,
                crossAxisSpacing: 10f
            ).GetLayout(650f);

            Assert.AreEqual(4, layout.CrossAxisCount);
            Assert.AreEqual(155f, layout.CellCrossAxisExtent, 0.01f);
        }

        [Test]
        public void IndexMapping_IsDeterministicRowMajor()
        {
            var layout = new SliverGridDelegateWithFixedCrossAxisCount(3).GetLayout(300f);

            Assert.AreEqual(0, layout.RowOf(0));
            Assert.AreEqual(0, layout.ColumnOf(0));
            Assert.AreEqual(0, layout.RowOf(2));
            Assert.AreEqual(2, layout.ColumnOf(2));
            Assert.AreEqual(1, layout.RowOf(3));
            Assert.AreEqual(0, layout.ColumnOf(3));
            Assert.AreEqual(2, layout.RowOf(7));
            Assert.AreEqual(1, layout.ColumnOf(7));
        }

        [Test]
        public void RowCount_RoundsUp()
        {
            var layout = new SliverGridDelegateWithFixedCrossAxisCount(3).GetLayout(300f);

            Assert.AreEqual(0, layout.RowCount(0));
            Assert.AreEqual(1, layout.RowCount(1));
            Assert.AreEqual(1, layout.RowCount(3));
            Assert.AreEqual(2, layout.RowCount(4));
            Assert.AreEqual(3, layout.RowCount(7));
        }

        [Test]
        public void Constructors_RejectInvalidArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SliverGridDelegateWithFixedCrossAxisCount(0)
            );
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SliverGridDelegateWithMaxCrossAxisExtent(0f)
            );
        }
    }
}
