using System.Text.RegularExpressions;
using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    // What RenderFlex reports, and what it does to the layout while reporting it. Two of its four
    // sites also repair, and they disagree with each other, so the geometry is pinned here alongside
    // the messages: the geometry is the part that must survive a refactor of the reporting, and the
    // part that has to break, loudly, when the repair policy changes.
    //
    // Mounted rather than driven off a FakeState, unlike RenderFlexTests: a report names the states
    // involved and walks the tree, so a state that throws on every member cannot reach these paths.
    public class RenderFlexDiagnosticsTests
    {
        private const float Inf = float.PositiveInfinity;

        private static RenderFlex MountRow(params Widget[] children)
        {
            var row = new Row();
            row.Children.AddRange(children);
            return (RenderFlex)TestHarness.Mount(row).RenderObject;
        }

        private static Widget Box(float width, float height) =>
            new FixedSizeBox { Size = new Vector2(width, height) };

        // Site 1: an inflexible child is measured against an unbounded main axis, so a child that
        // wants "as much as there is" answers with infinity. Clamped, and marked for the stripe.
        [Test]
        public void InflexibleChild_AnsweringInfinity_IsClampedToZeroAndMarked()
        {
            var row = MountRow(Box(Inf, 10));

            LogAssert.Expect(LogType.Error, new Regex("Returned infinite width"));
            row.Layout(LayoutConstraints.Loose(100, 10));

            Assert.AreEqual(new Vector2(0, 10), row.ChildrenLayout[0].Size);
            Assert.IsNotNull(row.ChildrenLayout[0].DebugWarning);
        }

        // Site 2: the inflexible children do not fit. The overflow is absorbed by giving the flexible
        // children nothing, and one child is marked -- the last inflexible one, which is a position
        // rather than a cause: here it is the 5px box, next to a 200px box in a 100px row.
        [Test]
        public void Overflow_ZeroesTheFlexSpace_AndMarksTheLastInflexibleChild()
        {
            var row = MountRow(Box(200, 10), Box(5, 10), new Expanded { Child = Box(0, 10) });

            LogAssert.Expect(LogType.Warning, new Regex("overflowed by 105"));
            row.Layout(LayoutConstraints.Tight(100, 10));

            Assert.AreEqual(0f, row.ChildrenLayout[2].Size.x, "flex children absorb the overflow");
            Assert.IsNull(row.ChildrenLayout[0].DebugWarning, "the 200px child is not blamed");
            StringAssert.Contains("overflowed", row.ChildrenLayout[1].DebugWarning);
        }

        // Site 3: flexible children need a bounded main axis to divide, and there is none. Reported
        // twice -- the complaint, then the tree dump -- and nothing is repaired, so the row answers
        // with the infinity it was handed.
        [Test]
        public void UnboundedMainAxis_WithFlexChildren_ReportsTwiceAndRepairsNothing()
        {
            var row = MountRow(Box(20, 10), new Expanded { Child = Box(5, 10) });

            LogAssert.Expect(LogType.Error, new Regex("unbounded width constraints"));
            LogAssert.Expect(LogType.Error, new Regex("RowState"));
            LogAssert.Expect(LogType.Error, new Regex("returned an infinite"));
            row.Layout(new LayoutConstraints(0, 0, Inf, 10));

            Assert.IsTrue(float.IsPositiveInfinity(row.Size.x));
        }

        // Site 4: the same fault as site 1, seen on a flexible child instead of an inflexible one --
        // and left alone, where site 1 clamps. One method, two answers.
        [Test]
        public void FlexChild_AnsweringInfinity_IsNeitherClampedNorMarked()
        {
            var row = MountRow(new Expanded { Child = Box(5, 10) });

            LogAssert.Expect(LogType.Error, new Regex("unbounded width constraints"));
            LogAssert.Expect(LogType.Error, new Regex("RowState"));
            LogAssert.Expect(LogType.Error, new Regex("returned an infinite"));
            row.Layout(new LayoutConstraints(0, 0, Inf, 10));

            Assert.IsTrue(float.IsPositiveInfinity(row.ChildrenLayout[0].Size.x));
            Assert.IsNull(row.ChildrenLayout[0].DebugWarning);
        }

        // Below the tolerance band nothing is said and nothing is marked, which is what keeps a
        // pixel of rounding from reading as a fault.
        [Test]
        public void OverflowWithinTolerance_IsNeitherReportedNorMarked()
        {
            var row = MountRow(Box(100.2f, 10));

            row.Layout(LayoutConstraints.Tight(100, 10));

            Assert.IsNull(row.ChildrenLayout[0].DebugWarning);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
