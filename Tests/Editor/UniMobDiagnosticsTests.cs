using System.Linq;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Diagnostics;
using UniMob.UI.Rendering;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class UniMobDiagnosticsTests
    {
        private sealed class OwnerState : FakeState { }

        // A leaf that reports on demand, so the latch can be driven directly instead of through a
        // real layout fault -- which no algorithm routes through the facades yet.
        private sealed class FaultyBox : LeafRenderObject
        {
            public bool Faulty { get; set; } = true;

            public FaultyBox()
                : base(new OwnerState()) { }

            protected override Vector2 PerformSizing(LayoutConstraints constraints)
            {
                if (this.Faulty)
                {
                    ReportOverflow(LayoutAxes.Horizontal, 60f, culpritIndex: -1, "make room");
                }

                return Vector2.zero;
            }

            protected override float ComputeIntrinsicWidth(float height) => 0f;

            protected override float ComputeIntrinsicHeight(float width) => 0f;
        }

        private static LayoutIssue AnyIssue(LayoutIssueCode code = LayoutIssueCode.Overflow) =>
            new LayoutIssue(code, subject: null, LayoutAxes.Horizontal, remedy: null, amount: 60f);

        [Test]
        public void Report_ReachesTheInstalledReporter()
        {
            using var log = RecordingReporter.Capture();

            UniMobDiagnostics.Report(AnyIssue());

            Assert.AreEqual(LayoutIssueCode.Overflow, log.Single().Code);
        }

        // There is no setter, so a fake left installed is unrepresentable rather than merely
        // discouraged -- which matters for a process-global static in an editor that never restarts.
        [Test]
        public void Override_RestoresThePreviousReporter_OnDispose()
        {
            var before = UniMobDiagnostics.Reporter;

            using (RecordingReporter.Capture())
            {
                Assert.AreNotSame(before, UniMobDiagnostics.Reporter);
            }

            Assert.AreSame(before, UniMobDiagnostics.Reporter);
        }

        [Test]
        public void Override_Nests_RestoringInOrder()
        {
            using var outer = RecordingReporter.Capture();

            using (var inner = RecordingReporter.Capture())
            {
                UniMobDiagnostics.Report(AnyIssue(LayoutIssueCode.NonFiniteSize));
                Assert.AreEqual(1, inner.Count);
            }

            UniMobDiagnostics.Report(AnyIssue());

            Assert.AreEqual(1, outer.Count, "the outer recorder should only see the second report");
            Assert.AreEqual(LayoutIssueCode.Overflow, outer.Single().Code);
        }

        // The console is edge-triggered and the overlay is level-triggered, and this is the assertion
        // that keeps them apart. An earlier design only noticed a fault when it also emitted one,
        // which reads identically over one pass and degenerates over several: it re-arms itself on
        // every suppressed pass, so it logs every other pass forever and HasLayoutIssue blinks.
        [Test]
        public void Latch_ReportsOnce_ButStaysTrue_WhileTheFaultPersists()
        {
            using var log = RecordingReporter.Capture();
            var box = new FaultyBox();

            for (var pass = 1; pass <= 5; pass++)
            {
                box.Layout(LayoutConstraints.Tight(pass * 10, 10));

                Assert.IsTrue(box.HasLayoutIssue, $"pass {pass} stopped noticing an ongoing fault");
                Assert.AreEqual(
                    1,
                    log.Count,
                    $"pass {pass} reported a fault it had already reported"
                );
            }
        }

        [Test]
        public void Latch_ArmsAgain_OnlyWhenTheFaultStops()
        {
            using var log = RecordingReporter.Capture();
            var box = new FaultyBox();

            box.Layout(LayoutConstraints.Tight(10, 10));
            Assert.AreEqual(1, log.Count);

            box.Faulty = false;
            box.Layout(LayoutConstraints.Tight(20, 10));
            Assert.IsFalse(box.HasLayoutIssue, "the fault stopped but the level signal did not");

            box.Faulty = true;
            box.Layout(LayoutConstraints.Tight(30, 10));

            Assert.AreEqual(2, log.Count, "a fault that came back should be reported again");
        }

        [Test]
        public void Latch_IsPerRenderObject()
        {
            using var log = RecordingReporter.Capture();

            new FaultyBox().Layout(LayoutConstraints.Tight(10, 10));
            new FaultyBox().Layout(LayoutConstraints.Tight(10, 10));

            Assert.AreEqual(2, log.Count);
        }
    }
}
