using System.Text.RegularExpressions;
using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;
using UnityEngine.TestTools;

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

        // Matched loosely on purpose: the assertion is that the box refuses the axis and says how to
        // give it one, not that the sentence keeps its wording.
        private static readonly Regex UnboundedComplaint = new Regex(
            "AnchoredBox was given unbounded .*Positioned"
        );

        [Test]
        public void UnboundedWidth_IsReported()
        {
            LogAssert.Expect(LogType.Error, UnboundedComplaint);

            Box().Layout(new LayoutConstraints(0, 0, float.PositiveInfinity, 200));
        }

        [Test]
        public void UnboundedHeight_IsReported()
        {
            LogAssert.Expect(LogType.Error, UnboundedComplaint);

            Box().Layout(new LayoutConstraints(0, 0, 200, float.PositiveInfinity));
        }

        [Test]
        public void FullyUnboundedConstraints_AreReportedOnce()
        {
            LogAssert.Expect(LogType.Error, UnboundedComplaint);

            Box().Layout(LayoutConstraints.Unbounded());
        }

        /// <summary>
        ///     The shape every anchored box in the app is actually built with. It must stay silent,
        ///     or the diagnostic is noise and will be muted.
        /// </summary>
        [Test]
        public void ATightFrame_IsNotReported()
        {
            var box = Box();

            box.Layout(LayoutConstraints.Tight(400, 300));

            Assert.AreEqual(new Vector2(400, 300), box.Size);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ALooseButBoundedFrame_IsNotReported()
        {
            var box = Box();

            box.Layout(LayoutConstraints.Loose(400, 300));

            Assert.AreEqual(
                new Vector2(400, 300),
                box.Size,
                "the box fills whatever bounded room it is given: that room is the keep-inside frame."
            );
            LogAssert.NoUnexpectedReceived();
        }
    }
}
