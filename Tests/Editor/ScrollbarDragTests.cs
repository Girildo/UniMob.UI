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
    ///     What dragging a <see cref="Scrollbar" />'s thumb and tapping its track do to the controller.
    /// </summary>
    /// <remarks>
    ///     Each pointer delta is mapped against the metrics current at that update, never against an
    ///     absolute thumb-to-offset mapping taken when the drag began. A lazy list's content extent is
    ///     an estimate that refines between passes, and an absolute mapping would jump the list
    ///     backwards the moment the estimate grew under a finger that had not moved.
    /// </remarks>
    public class ScrollbarDragTests
    {
        private const float Track = 300f;
        private const float Thickness = 8f;
        private const float Viewport = 300f;
        private const float Content = 1200f;
        private const float ThumbDelta = 4f;

        private readonly List<State> _mounted = new List<State>();

        private LifetimeController _lifetime = null!;
        private ScrollController _controller = null!;
        private TestZone _zone = null!;

        [SetUp]
        public void SetUp()
        {
            // A clock for the whole fixture: a bar's show-while-scrolling effect registers a ticker,
            // and a queued effect left behind would run against the next fixture's clock instead.
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
        public void EachUpdate_IsMappedAgainstTheMetricsOfItsOwnStep()
        {
            var fake = Attach();
            var (state, bar, render) = MountBar();

            var previous = _controller.PixelOffset;

            for (var step = 0; step < 4; step++)
            {
                // The estimate moves under the finger, in both directions, exactly as a lazy list's
                // does as it measures more of itself.
                fake.SetContentExtent(step % 2 == 0 ? Content : Content * 0.75f);
                TestHarness.Layout(state, LayoutConstraints.Tight(Thickness, Track));

                var metrics = _controller.Metrics!.Value;
                var thumbExtent = render.Thumb!.Value.Extent;
                var expected = Mathf.Clamp(
                    metrics.PixelOffset
                        + ScrollbarGeometry.PixelDeltaForThumbDelta(
                            metrics,
                            Track,
                            thumbExtent,
                            ThumbDelta
                        ),
                    0f,
                    metrics.MaxScrollExtent
                );

                ThumbOf(bar).OnDragUpdate!(Drag(ThumbDelta));

                Assert.AreEqual(
                    expected,
                    _controller.PixelOffset,
                    0.001f,
                    $"step {step} dragged the thumb {ThumbDelta}px down a {Track}px track with a "
                        + $"{thumbExtent}px thumb over {metrics.ContentExtent}px of content, so it "
                        + $"stands for {expected - metrics.PixelOffset}px of scrolling"
                );
                Assert.GreaterOrEqual(
                    _controller.PixelOffset,
                    previous,
                    $"step {step} moved the list backwards on a downward drag: the mapping was taken "
                        + "against an extent from an earlier step rather than this one's"
                );

                previous = _controller.PixelOffset;
            }

            Assert.Greater(
                _controller.PixelOffset,
                0f,
                "four downward drags have to have moved the list somewhere"
            );
        }

        [Test]
        public void ADragOnABarWithNoMetrics_DoesNothing()
        {
            var (_, bar, _) = MountBar();

            ThumbOf(bar).OnDragUpdate!(Drag(ThumbDelta));

            Assert.AreEqual(
                0f,
                _controller.PixelOffset,
                0.001f,
                "nothing is attached, so there is no content the thumb could stand for a position in"
            );
        }

        [Test]
        public void ADragStart_StopsWhateverTheListWasDoing()
        {
            var fake = Attach();
            var (_, bar, _) = MountBar();

            _controller.PixelOffset = 120f;

            ThumbOf(bar).OnDragStart!(Drag(0f));

            Assert.AreEqual(
                1,
                fake.SnapCount,
                "grabbing the thumb has to land the list on its current offset; inertia or a running "
                    + "scroll animation would otherwise fight every update of the drag"
            );
            Assert.AreEqual(
                120f,
                _controller.PixelOffset,
                0.001f,
                "and grabbing it must not move the list"
            );
        }

        // -- Track taps --------------------------------------------------------------------------

        [Test]
        public void ATapPastTheThumb_PagesForward()
        {
            Attach();
            var (_, bar, _) = MountBar(ScrollbarVisibility.Always);

            bar.OnTrackTap!(Tap(Track - 10f));

            Assert.AreEqual(
                Viewport,
                _controller.PixelOffset,
                0.001f,
                "a tap below the thumb means 'show me the next screenful', which is one viewport on"
            );
        }

        [Test]
        public void ATapBeforeTheThumb_PagesBack()
        {
            Attach();
            var (state, bar, _) = MountBar(ScrollbarVisibility.Always);

            _controller.PixelOffset = Viewport;
            TestHarness.Layout(state, LayoutConstraints.Tight(Thickness, Track));

            bar.OnTrackTap!(Tap(4f));

            Assert.AreEqual(
                0f,
                _controller.PixelOffset,
                0.001f,
                "a tap above the thumb pages back by one viewport, and the start of the content is as "
                    + "far back as that goes"
            );
        }

        [Test]
        public void ATapOnTheThumb_DoesNothing()
        {
            Attach();
            var (state, bar, render) = MountBar(ScrollbarVisibility.Always);

            _controller.PixelOffset = Viewport;
            TestHarness.Layout(state, LayoutConstraints.Tight(Thickness, Track));

            var thumb = render.Thumb!.Value;

            bar.OnTrackTap!(Tap(thumb.Offset + thumb.Extent / 2f));

            Assert.AreEqual(
                Viewport,
                _controller.PixelOffset,
                0.001f,
                $"the tap landed inside the thumb, which spans {thumb.Offset}..{thumb.Offset + thumb.Extent}; "
                    + "paging from under the thumb would move the list away from the finger that "
                    + "meant to grab it"
            );
        }

        // -- Helpers -----------------------------------------------------------------------------

        private FakeScrollable Attach()
        {
            var fake = new FakeScrollable(_controller, Content, Viewport);
            _controller.Attach(fake);
            return fake;
        }

        private (State state, IScrollbarState bar, RenderScrollbar render) MountBar(
            ScrollbarVisibility visibility = ScrollbarVisibility.WhileScrolling
        )
        {
            var state = TestHarness.Mount(
                new Scrollbar { Controller = _controller, Visibility = visibility }
            );

            TestHarness.Layout(state, LayoutConstraints.Tight(Thickness, Track));
            _mounted.Add(state);

            return (state, (IScrollbarState)state, (RenderScrollbar)state.RenderObject);
        }

        private static IGestureDetectorState ThumbOf(IScrollbarState bar) =>
            (IGestureDetectorState)bar.Child!;

        private static DragDetails Drag(float alongAxis) =>
            new DragDetails { LocalDelta = new Vector2(0f, alongAxis) };

        private static TapDetails Tap(float alongAxis) =>
            new TapDetails { LocalPosition = new Vector2(Thickness / 2f, alongAxis) };
    }
}
