using System.Linq;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Diagnostics;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // The contract every push depends on: a render object answers with a size its constraints allow.
    // Flutter asserts this in the RenderBox size setter and treats a violation as fatal; this layer
    // inherited the protocol without the check, and code all over it assumes the contract holds --
    // RenderConstrainedBox returns its child's size verbatim, and several render objects skip
    // constraining a result they know came from a child they bounded themselves.
    public class SizeExceedsConstraintsTests
    {
        private const float Inf = float.PositiveInfinity;

        private sealed class OwnerState : FakeState { }

        /// <summary>Answers with whatever it was told to, constraints or no constraints.</summary>
        private sealed class DisobedientBox : LeafRenderObject
        {
            private readonly Vector2 _answer;

            /// <summary>The owner, which <see cref="RenderObject.Owner"/> keeps protected.</summary>
            public readonly IState State;

            public DisobedientBox(Vector2 answer)
                : this(answer, new OwnerState()) { }

            private DisobedientBox(Vector2 answer, IState owner)
                : base(owner)
            {
                _answer = answer;
                this.State = owner;
            }

            protected override Vector2 PerformSizing(LayoutConstraints constraints) => _answer;

            protected override float ComputeIntrinsicWidth(float height) => 0f;

            protected override float ComputeIntrinsicHeight(float width) => 0f;
        }

        private static LayoutIssue[] Violations(RecordingReporter log) =>
            log.Where(issue => issue.Code == LayoutIssueCode.SizeExceedsConstraints).ToArray();

        [Test]
        public void AnAnswerWithinTheMaximum_IsSilent()
        {
            using var log = RecordingReporter.Capture();

            new DisobedientBox(new Vector2(80, 80)).Layout(LayoutConstraints.Loose(100, 100));

            Assert.IsEmpty(log);
        }

        [Test]
        public void AnAnswerExactlyAtTheMaximum_IsSilent()
        {
            using var log = RecordingReporter.Capture();

            new DisobedientBox(new Vector2(100, 100)).Layout(LayoutConstraints.Loose(100, 100));

            Assert.IsEmpty(log);
        }

        [Test]
        public void AnAnswerTallerThanTheMaximum_IsReportedWithTheExcess()
        {
            using var log = RecordingReporter.Capture();

            new DisobedientBox(new Vector2(100, 198)).Layout(LayoutConstraints.Loose(100, 192));

            var issue = log.Single();
            Assert.AreEqual(LayoutIssueCode.SizeExceedsConstraints, issue.Code);
            Assert.AreEqual(LayoutAxes.Vertical, issue.Axes);
            Assert.AreEqual(6f, issue.Amount, 0.001f);
            Assert.IsNotEmpty(issue.Remedy);
        }

        [Test]
        public void AnAnswerOverBothAxes_NamesBothAndReportsTheWorst()
        {
            using var log = RecordingReporter.Capture();

            new DisobedientBox(new Vector2(140, 110)).Layout(LayoutConstraints.Loose(100, 100));

            var issue = log.Single();
            Assert.AreEqual(LayoutAxes.Both, issue.Axes);
            Assert.AreEqual(40f, issue.Amount, 0.001f);
        }

        // Under an unbounded maximum nothing can be too big, which is the case that made the tile bug
        // invisible: a Column measured against infinity answered honestly and was then placed in a
        // box that could not hold it.
        [Test]
        public void AnAnswerUnderAnUnboundedMaximum_IsSilent()
        {
            using var log = RecordingReporter.Capture();

            new DisobedientBox(new Vector2(5000, 5000)).Layout(LayoutConstraints.Unbounded());

            Assert.IsEmpty(log);
        }

        [Test]
        public void ASubPixelExcess_IsWithinToleranceAndSilent()
        {
            using var log = RecordingReporter.Capture();

            new DisobedientBox(new Vector2(100, 100.2f)).Layout(LayoutConstraints.Loose(100, 100));

            Assert.IsEmpty(log);
        }

        // A non-finite answer is already NonFiniteSize. Reporting it under a second name as well would
        // say the same thing twice in different words, and the more specific name is the useful one.
        [Test]
        public void ANonFiniteAnswer_IsReportedOnlyAsNonFinite()
        {
            using var log = RecordingReporter.Capture();

            new DisobedientBox(new Vector2(100, Inf)).Layout(LayoutConstraints.Loose(100, 100));

            Assert.IsEmpty(Violations(log));
            Assert.AreEqual(LayoutIssueCode.NonFiniteSize, log.Single().Code);
        }

        [Test]
        public void AViolatingRenderObject_IsTheOneMarkedForTheStripe()
        {
            using var log = RecordingReporter.Capture();

            var box = new DisobedientBox(new Vector2(100, 198));
            box.Layout(LayoutConstraints.Loose(100, 192));

            Assert.IsTrue(box.HasLayoutIssue);
            Assert.AreSame(box.State, log.Single().Subject, "the promise-breaker must be named");
        }

        [Test]
        public void APersistentViolation_ReportsOnceAndStaysVisible()
        {
            using var log = RecordingReporter.Capture();

            var box = new DisobedientBox(new Vector2(100, 500));

            for (var height = 100; height < 105; height++)
            {
                box.Layout(LayoutConstraints.Loose(100, height));
                Assert.IsTrue(box.HasLayoutIssue, $"stripe went out at height {height}");
            }

            Assert.AreEqual(1, Violations(log).Length);
        }

        [Test]
        public void SizeExceedsConstraints_ErrorsRatherThanWarns()
        {
            Assert.AreEqual(
                LogType.Error,
                LayoutIssueText.Severity(LayoutIssueCode.SizeExceedsConstraints)
            );
        }

        [Test]
        public void TheSummary_SaysHowMuchLargerAndOnWhichAxis()
        {
            using var log = RecordingReporter.Capture();

            new DisobedientBox(new Vector2(100, 198)).Layout(LayoutConstraints.Loose(100, 192));

            var summary = LayoutIssueText.Summary(log.Single());
            StringAssert.Contains("6.0px", summary);
            StringAssert.Contains("constraints", summary);
            StringAssert.Contains("vertical axis", summary);
        }
    }
}
