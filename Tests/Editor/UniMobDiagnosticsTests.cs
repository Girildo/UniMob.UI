using System.Linq;
using NUnit.Framework;
using UniMob.UI.Diagnostics;

namespace UniMob.UI.Tests
{
    public class UniMobDiagnosticsTests
    {
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
    }
}
