using System.Linq;
using NUnit.Framework;
using UniMob.UI.Diagnostics;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     That <see cref="RenderAnchoredBox"/> insists on a bounded frame.
    /// </summary>
    /// <remarks>
    ///     Its own size is the box "keep inside" refers to, so an unbounded axis does not merely make
    ///     it bigger: the clamp silently stops clamping, and the child is offered infinite room. The
    ///     failure then reads as a positioning bug wherever the child ends up, a long way from the
    ///     parent that forgot to give it a frame.
    ///     <para>
    ///         Reachable from ordinary code since a ZStack lays a positioned child out unbounded on
    ///         any axis it does not pin, so <c>Positioned { Left, Top }</c> around an anchored box
    ///         hands it exactly this. Every such site in the app today uses <c>Positioned.Fill</c>,
    ///         and these tests are what keeps the next one from being silent.
    ///     </para>
    ///     <para>
    ///         Asserted against a recording reporter rather than Unity's log. A silence assertion made
    ///         with <c>LogAssert.NoUnexpectedReceived</c> would go vacuous the moment reports stopped
    ///         reaching Unity's log -- passing just as happily while the box screamed into a recorder
    ///         nobody read -- and a test that stops testing without failing is worse than no test.
    ///     </para>
    /// </remarks>
    public class RenderAnchoredBoxTests
    {
        private class FakeAnchoredBoxState : FakeSingleChildLayoutState, IAnchoredBoxState
        {
            public WidgetGeometry AnchorGeometry { get; set; } = WidgetGeometry.Empty;
            public WidgetGeometry SelfGeometry { get; set; } = WidgetGeometry.Empty;
            public Alignment TargetAnchor { get; set; } = Alignment.BottomLeft;
            public Alignment ChildAnchor { get; set; } = Alignment.TopLeft;
            public Vector2 Offset { get; set; }
            public float? KeepInsidePadding { get; set; } = 36f;
            public bool MatchAnchorWidth { get; set; }
            public Axis? FlipToFit { get; set; }
        }

        private static RenderAnchoredBox Box() =>
            new RenderAnchoredBox(
                new FakeAnchoredBoxState
                {
                    Child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(30, 40) }),
                }
            );

        // Structural, not textual: the assertion is that the box refuses the axis and says how to
        // give it one, which survives the sentence being rewritten.
        [Test]
        public void UnboundedWidth_IsReported()
        {
            using var log = RecordingReporter.Capture();

            Box().Layout(new LayoutConstraints(0, 0, float.PositiveInfinity, 200));

            var issue = log.Single();
            Assert.AreEqual(LayoutIssueCode.UnboundedConstraint, issue.Code);
            Assert.AreEqual(LayoutAxes.Horizontal, issue.Axes);
            Assert.IsNotNull(issue.Remedy);
        }

        [Test]
        public void UnboundedHeight_IsReported()
        {
            using var log = RecordingReporter.Capture();

            Box().Layout(new LayoutConstraints(0, 0, 200, float.PositiveInfinity));

            Assert.AreEqual(LayoutAxes.Vertical, log.Single().Axes);
        }

        // Axis, not Axes: the enum the rest of layout uses cannot say "both", which is exactly what
        // this case is.
        [Test]
        public void FullyUnboundedConstraints_AreReportedOnce_NamingBothAxes()
        {
            using var log = RecordingReporter.Capture();

            Box().Layout(LayoutConstraints.Unbounded());

            Assert.AreEqual(LayoutAxes.Both, log.Single().Axes);
        }

        /// <summary>
        ///     The shape every anchored box in the app is actually built with. It must stay silent,
        ///     or the diagnostic is noise and will be muted.
        /// </summary>
        [Test]
        public void ATightFrame_IsNotReported()
        {
            using var log = RecordingReporter.Capture();
            var box = Box();

            box.Layout(LayoutConstraints.Tight(400, 300));

            Assert.AreEqual(new Vector2(400, 300), box.PeekSize());
            Assert.IsEmpty(log);
        }

        [Test]
        public void ALooseButBoundedFrame_IsNotReported()
        {
            using var log = RecordingReporter.Capture();
            var box = Box();

            box.Layout(LayoutConstraints.Loose(400, 300));

            Assert.AreEqual(
                new Vector2(400, 300),
                box.PeekSize(),
                "the box fills whatever bounded room it is given: that room is the keep-inside frame."
            );
            Assert.IsEmpty(log);
        }
    }
}
