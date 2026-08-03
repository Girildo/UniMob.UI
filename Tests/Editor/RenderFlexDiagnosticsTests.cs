using System.Linq;
using NUnit.Framework;
using UniMob.UI.Diagnostics;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Layout.Internal.Views;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // What RenderFlex reports, and what it does to the layout while reporting it. Two of its four
    // sites also repair, and they disagree with each other, so the geometry is pinned here alongside
    // the reports: the geometry is the part that must survive a refactor of the reporting, and the
    // part that has to break, loudly, when the repair policy changes.
    //
    // Mounted rather than driven off a FakeState, unlike RenderFlexTests: a report names the states
    // involved and walks the tree, so a state that throws on every member cannot reach these paths.
    public class RenderFlexDiagnosticsTests
    {
        private const float Inf = float.PositiveInfinity;

        private static (State state, RenderFlex render) MountRow(params Widget[] children)
        {
            var row = new Row();
            row.Children.AddRange(children);

            var state = TestHarness.Mount(row);
            return (state, (RenderFlex)state.RenderObject);
        }

        private static Widget Box(float width, float height) =>
            new FixedSizeBox { Size = new Vector2(width, height) };

        private static IState ChildOf(State row, int index) =>
            ((IMultiChildLayoutState)row).Children[index];

        // Site 1: an inflexible child is measured against an unbounded main axis, so a child that
        // wants "as much as there is" answers with infinity. Clamped, and marked for the stripe.
        [Test]
        public void InflexibleChild_AnsweringInfinity_IsClampedToZeroAndMarked()
        {
            using var log = RecordingReporter.Capture();
            var (state, row) = MountRow(Box(Inf, 10));

            row.Layout(LayoutConstraints.Loose(100, 10));

            var issue = log.Single();
            Assert.AreEqual(LayoutIssueCode.NonFiniteChildSize, issue.Code);
            Assert.AreEqual(LayoutAxes.Horizontal, issue.Axes);
            Assert.AreSame(ChildOf(state, 0), issue.Culprit);

            Assert.AreEqual(new Vector2(0, 10), row.ChildrenLayout[0].Size);
            Assert.AreEqual(LayoutIssueCode.NonFiniteChildSize, row.ChildrenLayout[0].Issue);
        }

        // Site 2: the inflexible children do not fit. The overflow is absorbed by giving the flexible
        // children nothing, and the biggest inflexible child is named -- the 200px box, not the 5px
        // one that merely happened to be last.
        [Test]
        public void Overflow_ZeroesTheFlexSpace_AndNamesTheLargestInflexibleChild()
        {
            using var log = RecordingReporter.Capture();
            var (state, row) = MountRow(
                Box(200, 10),
                Box(5, 10),
                new Expanded { Child = Box(0, 10) }
            );

            row.Layout(LayoutConstraints.Tight(100, 10));

            var issue = log.Single();
            Assert.AreEqual(LayoutIssueCode.Overflow, issue.Code);
            Assert.AreEqual(105f, issue.Amount, 0.01f);
            Assert.AreSame(ChildOf(state, 0), issue.Culprit, "the 200px child should be named");

            Assert.AreEqual(0f, row.ChildrenLayout[2].Size.x, "flex children absorb the overflow");
            Assert.AreEqual(LayoutIssueCode.Overflow, row.ChildrenLayout[0].Issue);
            Assert.IsNull(row.ChildrenLayout[1].Issue);
        }

        // Site 3: flexible children need a bounded main axis to divide, and there is none. The axis
        // itself is not repaired -- there is nothing to repair it to -- but site 4 fires in the same
        // pass, because the flex child was handed an infinite share, and that end is repaired. So the
        // row ends up the width of its inflexible child rather than infinite.
        [Test]
        public void UnboundedMainAxis_WithFlexChildren_IsReportedAtBothEnds()
        {
            using var log = RecordingReporter.Capture();
            var (_, row) = MountRow(Box(20, 10), new Expanded { Child = Box(5, 10) });

            row.Layout(new LayoutConstraints(0, 0, Inf, 10));

            Assert.AreEqual(
                new[] { LayoutIssueCode.UnboundedConstraint, LayoutIssueCode.NonFiniteChildSize },
                log.Select(issue => issue.Code).ToArray()
            );
            Assert.IsNotNull(log.First().Remedy, "an unbounded axis is fixed differently per site");
            Assert.AreEqual(20f, row.PeekSize().x, 0.01f);
        }

        // Site 4: the same fault as site 1, seen on a flexible child instead of an inflexible one,
        // and now given the same answer. One method, one repair.
        [Test]
        public void FlexChild_AnsweringInfinity_IsClampedToZero()
        {
            using var log = RecordingReporter.Capture();
            var (_, row) = MountRow(new Expanded { Child = Box(5, 10) });

            row.Layout(new LayoutConstraints(0, 0, Inf, 10));

            Assert.IsTrue(log.Any(issue => issue.Code == LayoutIssueCode.NonFiniteChildSize));
            Assert.AreEqual(new Vector2(0, 10), row.ChildrenLayout[0].Size);
        }

        // Below the tolerance band nothing is said and nothing is marked, which keeps a pixel of
        // rounding from reading as a fault. Asserted against the recorder rather than Unity's log:
        // once reports route through a reporter, a log-silence assertion passes whether the widget
        // is quiet or screaming.
        [Test]
        public void OverflowWithinTolerance_IsNeitherReportedNorMarked()
        {
            using var log = RecordingReporter.Capture();
            var (_, row) = MountRow(Box(100.2f, 10));

            row.Layout(LayoutConstraints.Tight(100, 10));

            Assert.IsEmpty(log);
            Assert.IsNull(row.ChildrenLayout[0].Issue);
        }
    }
}
