using System.Linq;
using NUnit.Framework;
using UniMob.UI.Diagnostics;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // The one check made of the finished geometry rather than of an algorithm's intentions. Every
    // other report trusts the same measurements the pass trusted, so a pass whose numbers are wrong
    // reports nothing while putting a child outside the box. This reads the result instead.
    public class ChildOutOfBoundsTests
    {
        private const string Remedy = "Give it more room.";

        private sealed class OwnerState : FakeState { }

        /// <summary>Answers with a fixed size and puts its one child wherever it is told.</summary>
        private sealed class Placer : MultiChildRenderObject
        {
            private readonly Vector2 _size;
            private readonly Vector2 _childPosition;
            private readonly Vector2 _childSize;
            private readonly bool _mayOverhang;

            public Placer(
                Vector2 size,
                Vector2 childPosition,
                Vector2 childSize,
                bool mayOverhang = false
            )
                : base(new OwnerState())
            {
                _size = size;
                _childPosition = childPosition;
                _childSize = childSize;
                _mayOverhang = mayOverhang;
            }

            protected override bool ChildrenMayOverhang => _mayOverhang;

            protected override Vector2 PerformSizing(LayoutConstraints constraints)
            {
                ChildrenLayoutBuffer.Clear();
                ChildrenLayoutBuffer.Add(new LayoutInfo { Size = _childSize });
                return constraints.Constrain(_size);
            }

            protected override void PerformPositioning(Vector2 size)
            {
                ChildrenLayoutBuffer[0] = new LayoutInfo
                {
                    Size = _childSize,
                    Position = _childPosition,
                };
            }

            protected override float ComputeIntrinsicWidth(float height) => 0f;

            protected override float ComputeIntrinsicHeight(float width) => 0f;

            public LayoutIssueCode? IssueOnChild => ChildrenLayout[0].Issue;
        }

        private static LayoutIssue[] OutOfBounds(RecordingReporter log) =>
            log.Where(issue => issue.Code == LayoutIssueCode.ChildOutOfBounds).ToArray();

        private static void Lay(Placer placer) =>
            placer.Layout(LayoutConstraints.Loose(1000, 1000));

        [Test]
        public void AChildInsideItsParent_IsSilent()
        {
            using var log = RecordingReporter.Capture();

            Lay(new Placer(new Vector2(100, 100), new Vector2(10, 10), new Vector2(50, 50)));

            Assert.IsEmpty(log);
        }

        [Test]
        public void AChildExactlyFillingItsParent_IsSilent()
        {
            using var log = RecordingReporter.Capture();

            Lay(new Placer(new Vector2(100, 100), Vector2.zero, new Vector2(100, 100)));

            Assert.IsEmpty(log);
        }

        [Test]
        public void AChildRunningPastTheBottom_IsReportedWithHowFar()
        {
            using var log = RecordingReporter.Capture();

            Lay(new Placer(new Vector2(100, 100), Vector2.zero, new Vector2(100, 130)));

            var issue = log.Single();
            Assert.AreEqual(LayoutIssueCode.ChildOutOfBounds, issue.Code);
            Assert.AreEqual(LayoutAxes.Vertical, issue.Axes);
            Assert.AreEqual(30f, issue.Amount, 0.001f);
            Assert.AreEqual(Remedy, issue.Remedy ?? Remedy);
        }

        // The case that started this: content taller than its box, centred, escapes at BOTH edges. A
        // check that only looked at the far edge would miss half of it and understate the rest.
        [Test]
        public void ACentredChildTooBigForItsParent_IsCaughtAtTheNearEdge()
        {
            using var log = RecordingReporter.Capture();

            Lay(new Placer(new Vector2(100, 100), new Vector2(0, -4), new Vector2(100, 108)));

            var issue = log.Single();
            Assert.AreEqual(LayoutAxes.Vertical, issue.Axes);
            Assert.AreEqual(4f, issue.Amount, 0.001f);
        }

        [Test]
        public void AChildPushedOutSideways_NamesTheHorizontalAxis()
        {
            using var log = RecordingReporter.Capture();

            Lay(new Placer(new Vector2(100, 100), new Vector2(60, 0), new Vector2(50, 100)));

            Assert.AreEqual(LayoutAxes.Horizontal, log.Single().Axes);
        }

        [Test]
        public void AChildOutOnBothAxes_NamesBothAndReportsTheWorst()
        {
            using var log = RecordingReporter.Capture();

            Lay(new Placer(new Vector2(100, 100), Vector2.zero, new Vector2(120, 150)));

            var issue = log.Single();
            Assert.AreEqual(LayoutAxes.Both, issue.Axes);
            Assert.AreEqual(50f, issue.Amount, 0.001f);
        }

        [Test]
        public void ASubPixelOverhang_IsWithinToleranceAndSilent()
        {
            using var log = RecordingReporter.Capture();

            Lay(new Placer(new Vector2(100, 100), Vector2.zero, new Vector2(100, 100.2f)));

            Assert.IsEmpty(log);
        }

        // A scrollable, a stack with positioned children and an anchored box all place children outside
        // themselves on purpose. Reporting those would make the check useless within one screen.
        [Test]
        public void ARenderObjectThatMayOverhang_IsSilent()
        {
            using var log = RecordingReporter.Capture();

            Lay(
                new Placer(
                    new Vector2(100, 100),
                    new Vector2(0, -500),
                    new Vector2(100, 5000),
                    mayOverhang: true
                )
            );

            Assert.IsEmpty(log);
        }

        // The console entry is edge-triggered, but the in-scene marker has to stay on: it is the only
        // thing that says which child of which widget, in a tree where nothing else looks wrong.
        [Test]
        public void TheOffendingChild_IsMarkedForTheInSceneStripe()
        {
            using var log = RecordingReporter.Capture();

            var placer = new Placer(new Vector2(100, 100), Vector2.zero, new Vector2(100, 130));
            Lay(placer);

            Assert.AreEqual(LayoutIssueCode.ChildOutOfBounds, placer.IssueOnChild);
            Assert.IsTrue(placer.HasLayoutIssue);
        }

        [Test]
        public void APersistentOverhang_ReportsOnceAndStaysVisible()
        {
            using var log = RecordingReporter.Capture();

            var placer = new Placer(new Vector2(100, 100), Vector2.zero, new Vector2(100, 130));

            for (var width = 1000; width < 1005; width++)
            {
                placer.Layout(LayoutConstraints.Loose(width, 1000));
                Assert.IsTrue(placer.HasLayoutIssue, $"stripe went out at width {width}");
            }

            Assert.AreEqual(1, OutOfBounds(log).Length);
        }

        [Test]
        public void ChildOutOfBounds_WarnsRatherThanErrors()
        {
            Assert.AreEqual(
                LogType.Warning,
                LayoutIssueText.Severity(LayoutIssueCode.ChildOutOfBounds)
            );
        }

        [Test]
        public void TheSummary_SaysHowFarOutsideWhichWidget()
        {
            using var log = RecordingReporter.Capture();

            Lay(new Placer(new Vector2(100, 100), Vector2.zero, new Vector2(100, 130)));

            var summary = LayoutIssueText.Summary(log.Single());
            StringAssert.Contains("30.0px", summary);
            StringAssert.Contains("outside", summary);
            StringAssert.Contains("vertical axis", summary);
        }
    }
}
