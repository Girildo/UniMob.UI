using System.Linq;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Diagnostics;
using UniMob.UI.Rendering;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // The materialise rule, as a checked postcondition: an infinite size out is legal exactly when
    // the constraint in was infinite on that axis.
    public class RenderObjectValidationTests
    {
        private const float Inf = float.PositiveInfinity;

        private sealed class OwnerState : FakeState { }

        // Deliberately does not Constrain what it returns, which is the fault being detected.
        private sealed class UnconstrainedBox : LeafRenderObject
        {
            private readonly Vector2 _answer;

            public UnconstrainedBox(Vector2 answer)
                : base(new OwnerState()) => _answer = answer;

            protected override Vector2 PerformSizing(LayoutConstraints constraints) => _answer;

            protected override float ComputeIntrinsicWidth(float height) => 0f;

            protected override float ComputeIntrinsicHeight(float width) => 0f;
        }

        [Test]
        public void InfiniteWidth_UnderABoundedMaximum_IsReportedOnThatAxisAlone()
        {
            using var log = RecordingReporter.Capture();

            new UnconstrainedBox(new Vector2(Inf, 10)).Layout(LayoutConstraints.Loose(100, 100));

            var issue = log.Single();
            Assert.AreEqual(LayoutIssueCode.NonFiniteSize, issue.Code);
            Assert.AreEqual(LayoutAxes.Horizontal, issue.Axes);
            Assert.IsNotNull(issue.Remedy);
        }

        // Passing infinity along under infinity is passing the buck upward, and stays legal until it
        // reaches a RectTransform -- which is somewhere else's problem to notice.
        [Test]
        public void InfiniteWidth_UnderAnUnboundedMaximum_IsSilent()
        {
            using var log = RecordingReporter.Capture();

            new UnconstrainedBox(new Vector2(Inf, 10)).Layout(LayoutConstraints.Unbounded());

            Assert.IsEmpty(log);
        }

        [Test]
        public void BothAxesNonFinite_UnderABoundedMaximum_NamesBoth()
        {
            using var log = RecordingReporter.Capture();

            new UnconstrainedBox(new Vector2(Inf, Inf)).Layout(LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(LayoutAxes.Both, log.Single().Axes);
        }

        // "Non-finite", not "infinite": the fault is a number layout cannot use, and NaN is one.
        [Test]
        public void NaN_IsTreatedAsNonFinite()
        {
            using var log = RecordingReporter.Capture();

            new UnconstrainedBox(new Vector2(10, float.NaN)).Layout(
                LayoutConstraints.Loose(100, 100)
            );

            Assert.AreEqual(LayoutAxes.Vertical, log.Single().Axes);
        }

        [Test]
        public void AFiniteAnswer_IsSilent()
        {
            using var log = RecordingReporter.Capture();

            new UnconstrainedBox(new Vector2(10, 10)).Layout(LayoutConstraints.Loose(100, 100));

            Assert.IsEmpty(log);
        }
    }
}
