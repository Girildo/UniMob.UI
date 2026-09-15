using System.Collections.Generic;
using NUnit.Framework;
using UniMob.Core;
using UniMob.UI.Internal;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     When a <see cref="Scrollbar" /> is on screen, and what it costs the clock when it is not.
    /// </summary>
    /// <remarks>
    ///     A bar that has faded out must leave nothing registered: a countdown that keeps ticking
    ///     forever holds a frame-rate policy awake for as long as any list is mounted, and no fixture
    ///     hosting one could ever settle. That is why every assertion here is paired with
    ///     <see cref="TestZone.IsSettled" />.
    /// </remarks>
    public class ScrollbarVisibilityTests
    {
        private const float Track = 300f;
        private const float Thickness = 8f;
        private const float FadeDelay = 0.6f;
        private const float FadeDuration = 0.3f;

        // Long enough for the fade to have finished, with a couple of frames to spare.
        private const float FadeSettleTime = FadeDuration + 0.1f;

        private readonly List<State> _mounted = new List<State>();

        private LifetimeController _lifetime = null!;
        private ScrollController _controller = null!;
        private TestZone _zone = null!;

        [SetUp]
        public void SetUp()
        {
            _zone = TestZone.Install();
            _lifetime = new LifetimeController();
            _controller = new ScrollController(_lifetime.Lifetime);
        }

        [TearDown]
        public void TearDown()
        {
            // Unmounted before the controller is disposed. A bar left mounted keeps a live reaction
            // on the controller's atoms, and the next fixture's AtomScheduler.Sync() then actualizes
            // it against a disposed lifetime.
            foreach (var state in _mounted)
            {
                StateUtilities.DeactivateChild(state);
            }

            _mounted.Clear();
            AtomScheduler.Sync();
            _lifetime.Dispose();
            _zone.Dispose();
        }

        [Test]
        public void AnUntouchedList_ShowsNoBar_AndRegistersNothing()
        {
            var zone = _zone;
            var bar = MountBar();

            AtomScheduler.Sync();

            Assert.AreEqual(
                0f,
                bar.Opacity.Value,
                0.001f,
                "nothing has scrolled, so there is nothing to report the position of"
            );
            Assert.IsFalse(
                bar.BlocksPointer,
                "an invisible strip lying over a list must not take the drags meant for the list"
            );
            Assert.IsNull(
                bar.OnTrackTap,
                "a track nobody can see is not somewhere a tap can mean anything"
            );
            Assert.IsTrue(
                zone.IsSettled,
                "a scrollbar at rest must register no ticker at all; one that polls forever keeps the "
                    + "clock awake for as long as any list is mounted"
            );
        }

        [Test]
        public void Scrolling_ShowsTheBar_AndItFadesOutAfterTheDelay()
        {
            var zone = _zone;
            var bar = MountBar();

            Scroll(10f);
            zone.PumpFor(FadeSettleTime);

            Assert.AreEqual(
                1f,
                bar.Opacity.Value,
                0.001f,
                $"the list moved {FadeSettleTime}s ago and the fade takes {FadeDuration}s, so the bar "
                    + "is fully up"
            );
            Assert.IsTrue(bar.BlocksPointer, "a visible thumb has to be grabbable");
            Assert.IsNotNull(bar.OnTrackTap, "and a visible track has to be tappable");

            zone.PumpFor(FadeDelay + FadeDuration);
            zone.Settle();

            Assert.AreEqual(
                0f,
                bar.Opacity.Value,
                0.001f,
                $"nothing moved for {FadeDelay}s, so the bar went away again"
            );
            Assert.IsFalse(
                bar.BlocksPointer,
                "and it stopped taking input on the way out, rather than leaving an invisible strip "
                    + "over the list"
            );
            Assert.IsNull(bar.OnTrackTap);
            Assert.IsTrue(
                zone.IsSettled,
                "a faded bar must have dropped its countdown; Settle() would have thrown first if it "
                    + "had not"
            );
        }

        [Test]
        public void ASecondScroll_RestartsTheCountdown()
        {
            var zone = _zone;
            var bar = MountBar();

            Scroll(10f);
            zone.PumpFor(FadeSettleTime);

            Scroll(20f);
            zone.PumpFor(0.5f);

            Assert.AreEqual(
                1f,
                bar.Opacity.Value,
                0.001f,
                $"0.5s after the second scroll is inside the {FadeDelay}s delay, but "
                    + $"{FadeSettleTime + 0.5f}s after the first: a countdown that was not restarted "
                    + "would have taken the bar away mid-scroll"
            );
        }

        [Test]
        public void ADragHoldsTheBarUp_UntilItEnds()
        {
            var zone = _zone;
            var bar = MountBar();

            Scroll(10f);
            zone.PumpFor(FadeSettleTime);

            var thumb = ThumbOf(bar);
            thumb.OnDragStart!(new DragDetails());

            zone.PumpFor(2f);

            Assert.AreEqual(
                1f,
                bar.Opacity.Value,
                0.001f,
                $"a finger has been on the thumb for 2s, well past the {FadeDelay}s delay: fading out "
                    + "from under the pointer dragging it is the one time the bar must not go"
            );

            thumb.OnDragEnd!(new DragDetails());

            zone.PumpFor(FadeDelay + FadeDuration);
            zone.Settle();

            Assert.AreEqual(
                0f,
                bar.Opacity.Value,
                0.001f,
                "and the countdown resumes when the finger leaves"
            );
            Assert.IsTrue(zone.IsSettled);
        }

        [Test]
        public void AnAlwaysBar_StartsUp_AndNeverCountsDown()
        {
            var zone = _zone;
            var bar = MountBar(ScrollbarVisibility.Always);

            AtomScheduler.Sync();

            Assert.AreEqual(
                1f,
                bar.Opacity.Value,
                0.001f,
                "an Always bar is up before anything has scrolled, with no fade to run first"
            );
            Assert.IsTrue(bar.BlocksPointer);
            Assert.IsNotNull(bar.OnTrackTap);
            Assert.IsTrue(
                zone.IsSettled,
                "a bar that never hides has nothing to count down, and must register no ticker for it"
            );

            Scroll(10f);
            zone.PumpFor(2f);

            Assert.AreEqual(
                1f,
                bar.Opacity.Value,
                0.001f,
                "2s of stillness is no reason for an Always bar to leave"
            );
            Assert.IsTrue(zone.IsSettled, "and it registered nothing while it was scrolled either");
        }

        [Test]
        public void PageOnTrackTapOff_LeavesTheTrackInert()
        {
            var zone = _zone;
            var bar = MountBar(ScrollbarVisibility.Always, pageOnTrackTap: false);

            AtomScheduler.Sync();

            Assert.IsNull(
                bar.OnTrackTap,
                "the bar is fully visible, so only PageOnTrackTap can be what keeps the track inert"
            );

            Scroll(10f);
            zone.PumpFor(FadeSettleTime);

            Assert.IsNull(bar.OnTrackTap, "and scrolling does not turn it on");
        }

        // -- Helpers -----------------------------------------------------------------------------

        private IScrollbarState MountBar(
            ScrollbarVisibility visibility = ScrollbarVisibility.WhileScrolling,
            bool pageOnTrackTap = true
        )
        {
            _controller.Attach(
                new FakeScrollable(_controller, contentExtent: 1200f, viewportExtent: Track)
            );

            var state = TestHarness.Mount(
                new Scrollbar
                {
                    Controller = _controller,
                    Visibility = visibility,
                    FadeDelay = FadeDelay,
                    FadeDuration = FadeDuration,
                    PageOnTrackTap = pageOnTrackTap,
                }
            );

            TestHarness.Layout(state, LayoutConstraints.Tight(Thickness, Track));
            _mounted.Add(state);

            return (IScrollbarState)state;
        }

        private void Scroll(float pixelOffset)
        {
            _controller.PixelOffset = pixelOffset;
            AtomScheduler.Sync();
        }

        private static IGestureDetectorState ThumbOf(IScrollbarState bar) =>
            (IGestureDetectorState)bar.Child!;
    }
}
