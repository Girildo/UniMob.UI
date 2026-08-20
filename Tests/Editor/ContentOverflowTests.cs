using System.Linq;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Diagnostics;
using UniMob.UI.Rendering;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // Content that does not fit the constraints it was given. The fault is invisible without a
    // report, because Constrain is Mathf.Clamp: the shortfall exists for one expression and this
    // layout layer does not clip, so an overflowing widget is simply drawn outside its box.
    public class ContentOverflowTests
    {
        private const float Inf = float.PositiveInfinity;

        private const string Remedy = "Give it more room.";

        private sealed class OwnerState : FakeState { }

        /// <summary>Wants a fixed size, reports what it cannot have, then settles for what it can.</summary>
        private sealed class HungryBox : LeafRenderObject
        {
            private readonly Vector2 _desired;
            private readonly LayoutAxes _considered;

            public HungryBox(Vector2 desired, LayoutAxes considered = LayoutAxes.Both)
                : base(new OwnerState())
            {
                _desired = desired;
                _considered = considered;
            }

            protected override Vector2 PerformSizing(LayoutConstraints constraints)
            {
                ReportContentOverflow(constraints, _desired, Remedy, _considered);
                return constraints.Constrain(_desired);
            }

            protected override float ComputeIntrinsicWidth(float height) => 0f;

            protected override float ComputeIntrinsicHeight(float width) => 0f;
        }

        private static LayoutIssue[] ContentOverflows(RecordingReporter log) =>
            log.Where(issue => issue.Code == LayoutIssueCode.ContentOverflow).ToArray();

        [Test]
        public void ContentTallerThanTheMaximum_IsReportedWithTheShortfall()
        {
            using var log = RecordingReporter.Capture();

            new HungryBox(new Vector2(50, 150)).Layout(LayoutConstraints.Loose(100, 100));

            var issue = log.Single();
            Assert.AreEqual(LayoutIssueCode.ContentOverflow, issue.Code);
            Assert.AreEqual(LayoutAxes.Vertical, issue.Axes);
            Assert.AreEqual(50f, issue.Amount, 0.001f);
            Assert.AreEqual(Remedy, issue.Remedy);
        }

        [Test]
        public void ContentOverBothAxes_NamesBothAndReportsTheLargerShortfall()
        {
            using var log = RecordingReporter.Capture();

            new HungryBox(new Vector2(130, 180)).Layout(LayoutConstraints.Loose(100, 100));

            var issue = log.Single();
            Assert.AreEqual(LayoutAxes.Both, issue.Axes);
            Assert.AreEqual(80f, issue.Amount, 0.001f);
        }

        [Test]
        public void ContentThatFits_IsSilent()
        {
            using var log = RecordingReporter.Capture();

            new HungryBox(new Vector2(50, 50)).Layout(LayoutConstraints.Loose(100, 100));

            Assert.IsEmpty(log);
        }

        // Asking for infinity and being clamped to the maximum is how a render object says "fill". It
        // is the most common thing they do, and reporting it would drown the ones that mean something.
        [Test]
        public void AnInfiniteDesire_IsFillAndStaysSilent()
        {
            using var log = RecordingReporter.Capture();

            new HungryBox(new Vector2(Inf, Inf)).Layout(LayoutConstraints.Loose(100, 100));

            Assert.IsEmpty(log);
        }

        [Test]
        public void AnUnboundedMaximum_CannotBeOverflowed()
        {
            using var log = RecordingReporter.Capture();

            new HungryBox(new Vector2(5000, 5000)).Layout(LayoutConstraints.Unbounded());

            Assert.IsEmpty(log);
        }

        // Rounding is not an overflow. The threshold is the same one the flex overflow uses, and it
        // lives in the facade because it is a threshold for saying something, not for doing something.
        [Test]
        public void AShortfallWithinTolerance_IsSilent()
        {
            using var log = RecordingReporter.Capture();

            new HungryBox(new Vector2(100.2f, 100)).Layout(LayoutConstraints.Loose(100, 100));

            Assert.IsEmpty(log);
        }

        // NaN is a size layout cannot use at all, which is the postcondition's fault to name. Reporting
        // it here as well would say the same thing twice in different words.
        [Test]
        public void NaN_IsLeftToTheNonFinitePostcondition()
        {
            using var log = RecordingReporter.Capture();

            new HungryBox(new Vector2(50, float.NaN)).Layout(LayoutConstraints.Loose(100, 100));

            Assert.IsEmpty(ContentOverflows(log));
            Assert.AreEqual(LayoutIssueCode.NonFiniteSize, log.Single().Code);
        }

        [Test]
        public void AnExcludedAxis_IsNotReportedEvenWhenItOverflows()
        {
            using var log = RecordingReporter.Capture();

            new HungryBox(new Vector2(500, 50), LayoutAxes.Vertical).Layout(
                LayoutConstraints.Loose(100, 100)
            );

            Assert.IsEmpty(log);
        }

        [Test]
        public void AnExcludedAxis_DoesNotSuppressTheOtherOne()
        {
            using var log = RecordingReporter.Capture();

            new HungryBox(new Vector2(500, 150), LayoutAxes.Vertical).Layout(
                LayoutConstraints.Loose(100, 100)
            );

            var issue = log.Single();
            Assert.AreEqual(LayoutAxes.Vertical, issue.Axes);
            Assert.AreEqual(50f, issue.Amount, 0.001f);
        }

        // The console is edge-triggered and the in-scene marker is level-triggered: the log says a
        // fault started, the stripe says it is still happening. Getting this wrong reports on every
        // other pass and blinks the stripe at half frame rate.
        [Test]
        public void APersistentOverflow_ReportsOnceButStaysVisibleEveryPass()
        {
            using var log = RecordingReporter.Capture();

            var box = new HungryBox(new Vector2(50, 500));

            // Fresh constraints each time: equal constraints are cut off by the memo, so reusing them
            // would assert that no pass ran rather than that repeated passes stay quiet.
            for (var height = 100; height < 105; height++)
            {
                box.Layout(LayoutConstraints.Loose(100, height));
                Assert.IsTrue(box.HasLayoutIssue, $"stripe went out at height {height}");
            }

            Assert.AreEqual(1, ContentOverflows(log).Length);
        }

        [Test]
        public void AnOverflowThatIsFixed_ReportsAgainWhenItReturns()
        {
            using var log = RecordingReporter.Capture();

            var box = new HungryBox(new Vector2(50, 150));

            box.Layout(LayoutConstraints.Loose(100, 100));
            box.Layout(LayoutConstraints.Loose(100, 200));
            Assert.IsFalse(box.HasLayoutIssue, "the fault cleared, so the stripe should be gone");

            box.Layout(LayoutConstraints.Loose(100, 101));

            Assert.AreEqual(2, ContentOverflows(log).Length);
        }

        // An overflow still renders, wrongly and visibly. The non-finite faults are a widget that does
        // not render at all, and the two do not deserve the same volume.
        [Test]
        public void ContentOverflow_WarnsRatherThanErrors()
        {
            Assert.AreEqual(
                LogType.Warning,
                LayoutIssueText.Severity(LayoutIssueCode.ContentOverflow)
            );
        }

        // "Owner", not "OwnerState": a report names what the author wrote. Falling back to the state's
        // own type means stripping the suffix, so this pins that rule rather than merely the wording.
        [Test]
        public void TheSummary_NamesTheWidgetTheNumberAndTheAxis()
        {
            using var log = RecordingReporter.Capture();

            new HungryBox(new Vector2(50, 150)).Layout(LayoutConstraints.Loose(100, 100));

            var summary = LayoutIssueText.Summary(log.Single());
            StringAssert.Contains("Owner ", summary);
            StringAssert.DoesNotContain("OwnerState", summary);
            StringAssert.Contains("50.0px", summary);
            StringAssert.Contains("vertical axis", summary);
        }
    }
}
