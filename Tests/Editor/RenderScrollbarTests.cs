using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniMob.Core;
using UniMob.UI.Diagnostics;
using UniMob.UI.Internal;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     What a <see cref="Scrollbar" /> lays out: a track filling its box along its axis, and a
    ///     thumb placed on it from the attached scrollable's metrics.
    /// </summary>
    /// <remarks>
    ///     Driven through a fake executor rather than a real list, so the metrics are dictated rather
    ///     than estimated: every number here is the geometry the bar draws for a stated viewport and
    ///     content, and a list's own estimation is pinned elsewhere.
    /// </remarks>
    public class RenderScrollbarTests
    {
        private const float Track = 300f;
        private const float Viewport = 100f;
        private const float Content = 400f;
        private const float Thickness = 8f;

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

        // -- The track ---------------------------------------------------------------------------

        [Test]
        public void TightConstraints_AreObeyedOnBothAxes()
        {
            var render = MountBar();

            var size = render.Layout(LayoutConstraints.Tight(Thickness, Track));

            Assert.AreEqual(
                new Vector2(Thickness, Track),
                size,
                "a tightly constrained scrollbar is exactly the size it was given"
            );
        }

        [Test]
        public void LooseConstraints_FillTheAxis_AndHugTheThickness()
        {
            var render = MountBar();

            var size = render.Layout(LayoutConstraints.Loose(100f, Track));

            Assert.AreEqual(
                new Vector2(Thickness, Track),
                size,
                $"a vertical scrollbar fills its 300px axis and takes its own {Thickness}px across; "
                    + "filling the 100px the loose constraint allows would paint a bar over the list"
            );
        }

        [Test]
        public void AnUnboundedAxis_IsZeroed_AndReportedOnce()
        {
            using var log = RecordingReporter.Capture();
            var render = MountBar();

            var size = render.Layout(new LayoutConstraints(0f, 0f, 100f, float.PositiveInfinity));

            Assert.AreEqual(
                0f,
                size.y,
                0.001f,
                "there is no honest height for a track nothing bounds, so the axis is zeroed rather "
                    + "than invented"
            );
            Assert.AreEqual(
                Thickness,
                size.x,
                0.001f,
                "the cross axis is unaffected by the unbounded one"
            );

            var issue = log.Single();
            Assert.AreEqual(
                LayoutIssueCode.UnboundedConstraint,
                issue.Code,
                $"expected an unbounded-constraint report, got {issue.Code}"
            );
            Assert.AreEqual(
                LayoutAxes.Vertical,
                issue.Axes,
                "a vertical scrollbar is the vertical axis it cannot resolve"
            );
            StringAssert.Contains(
                "Positioned",
                issue.Remedy,
                "the remedy must name a way to bound the axis, or a reader is left with the diagnosis "
                    + "and no fix"
            );
        }

        [Test]
        public void AHorizontalBar_SwapsTheAxes()
        {
            var render = MountBar(Axis.Horizontal);
            Attach(Content, Viewport, Axis.Horizontal);

            var size = render.Layout(LayoutConstraints.Loose(Track, 100f));

            Assert.AreEqual(
                new Vector2(Track, Thickness),
                size,
                "a horizontal scrollbar fills the width and is Thickness tall"
            );
            Assert.AreEqual(
                new Vector2(ExpectedThumbExtent, Thickness),
                render.ChildSize,
                "the thumb runs along the width of a horizontal bar"
            );
        }

        // -- The thumb ---------------------------------------------------------------------------

        [Test]
        public void TheThumb_IsProportionalToTheVisibleFraction()
        {
            var render = MountBar();
            Attach(Content, Viewport);

            render.Layout(LayoutConstraints.Tight(Thickness, Track));

            Assert.AreEqual(
                new Vector2(Thickness, ExpectedThumbExtent),
                render.ChildSize,
                $"{Viewport} of {Content} is visible, so the thumb covers that fraction of the "
                    + $"{Track}px track"
            );
            Assert.AreEqual(
                Vector2.zero,
                render.ChildPosition,
                "an unscrolled list puts the thumb at the start of the track"
            );
        }

        [Test]
        public void TheThumb_TravelsWhatTheTrackLeavesFree()
        {
            var render = MountBar();
            Attach(Content, Viewport);

            _controller.PixelOffset = Content - Viewport;
            render.Layout(LayoutConstraints.Tight(Thickness, Track));

            Assert.AreEqual(
                Track - ExpectedThumbExtent,
                render.ChildPosition.y,
                0.001f,
                "at the end of the content the thumb's far edge is the track's far edge; anything "
                    + "else leaves the bar claiming there is more to come"
            );

            _controller.PixelOffset = (Content - Viewport) / 2f;
            render.Layout(LayoutConstraints.Tight(Thickness, Track));

            Assert.AreEqual(
                (Track - ExpectedThumbExtent) / 2f,
                render.ChildPosition.y,
                0.001f,
                "halfway through the scrollable range is halfway along the free travel, which is the "
                    + "track minus the thumb rather than the whole track"
            );
            Assert.AreEqual(
                new Vector2(Thickness, ExpectedThumbExtent),
                render.ChildSize,
                "scrolling does not resize the thumb"
            );
        }

        [Test]
        public void NoMetrics_DrawsNoThumb()
        {
            var render = MountBar();

            render.Layout(LayoutConstraints.Tight(Thickness, Track));

            Assert.IsNull(
                render.Thumb,
                "nothing is attached, so there is no geometry to place a thumb from"
            );
            Assert.AreEqual(
                Vector2.zero,
                render.ChildSize,
                "a bar with no thumb still lays its child out, at nothing: a child a pass skips is "
                    + "never laid out at all, and its view then paints an unmeasured box"
            );
        }

        [Test]
        public void ContentThatFits_DrawsNoThumb()
        {
            var render = MountBar();
            Attach(contentExtent: 80f, viewportExtent: Viewport);

            render.Layout(LayoutConstraints.Tight(Thickness, Track));

            Assert.IsNull(
                render.Thumb,
                "80px of content in a 100px viewport does not scroll, and a scrollbar for content "
                    + "that fits is noise"
            );
            Assert.AreEqual(Vector2.zero, render.ChildSize);
        }

        // -- What the bar answers about the pass it just drew -------------------------------------

        [Test]
        public void ScrollDeltaFor_MapsAThumbMovementAgainstTheLastPass()
        {
            var render = MountBar();
            Attach(Content, Viewport);

            render.Layout(LayoutConstraints.Tight(Thickness, Track));

            var expected = RenderScrollbar.ScrollDeltaForThumbDelta(
                _controller.Metrics!.Value,
                Track,
                ExpectedThumbExtent,
                4f
            );

            Assert.AreEqual(
                expected,
                render.ScrollDeltaFor(4f),
                0.001f,
                $"4px of thumb travel on a {Track}px track carrying a {ExpectedThumbExtent}px thumb "
                    + $"stands for {expected}px of scrolling"
            );
        }

        [Test]
        public void PageTargetFor_PagesAwayFromTheThumb_AndNowhereFromUnderIt()
        {
            var render = MountBar();
            Attach(Content, Viewport);

            _controller.PixelOffset = 150f;
            render.Layout(LayoutConstraints.Tight(Thickness, Track));

            var thumb = render.Thumb!.Value;

            Assert.IsNull(
                render.PageTargetFor(thumb.Offset + thumb.Extent / 2f),
                $"the tap landed inside the thumb, which spans "
                    + $"{thumb.Offset}..{thumb.Offset + thumb.Extent}; paging from under the thumb "
                    + "would move the list away from the finger that meant to grab it"
            );
            Assert.AreEqual(
                150f + Viewport,
                render.PageTargetFor(thumb.Offset + thumb.Extent + 1f)!.Value,
                0.001f,
                $"a tap past the thumb means 'the next screenful', which is one {Viewport}px viewport "
                    + "on from 150"
            );
            Assert.AreEqual(
                150f - Viewport,
                render.PageTargetFor(thumb.Offset - 1f)!.Value,
                0.001f,
                $"and a tap before it is one {Viewport}px viewport back from 150"
            );

            _controller.PixelOffset = 40f;
            render.Layout(LayoutConstraints.Tight(Thickness, Track));

            Assert.AreEqual(
                0f,
                render.PageTargetFor(0f)!.Value,
                0.001f,
                "paging back from 40 stops at the start of the content rather than at -60"
            );
        }

        // -- What scrolling costs the parent -----------------------------------------------------

        [Test]
        public void Scrolling_MovesTheThumb_WithoutResizingTheBar()
        {
            var render = MountBar();
            Attach(Content, Viewport);

            render.Layout(LayoutConstraints.Tight(Thickness, Track));

            var sizeRuns = 0;
            var layoutRuns = 0;

            // The Action overload on both, counting runs: the size never changes, so a value-diffing
            // reaction could not tell "watched and unmoved" apart from "never watched at all".
            Atom.Reaction(
                _lifetime.Lifetime,
                () =>
                {
                    sizeRuns++;
                    render.WatchedSize();
                }
            );
            Atom.Reaction(
                _lifetime.Lifetime,
                () =>
                {
                    layoutRuns++;
                    render.WatchLayout();
                }
            );

            AtomScheduler.Sync();

            var sizeRunsBefore = sizeRuns;
            var layoutRunsBefore = layoutRuns;

            _controller.PixelOffset = 150f;
            AtomScheduler.Sync();

            Assert.Greater(
                layoutRuns,
                layoutRunsBefore,
                "the thumb moved, so anything rendering this bar has to run again"
            );
            Assert.AreEqual(
                sizeRunsBefore,
                sizeRuns,
                "the track's size does not depend on the offset, so a parent measuring this bar must "
                    + "not be dragged into a relayout on every scroll"
            );
            Assert.AreEqual(
                (Track - ExpectedThumbExtent) / 2f,
                render.ChildPosition.y,
                0.001f,
                "and the pass that ran did move the thumb"
            );
        }

        // -- Intrinsics --------------------------------------------------------------------------

        [Test]
        public void Intrinsics_AreTheThicknessAcross_AndNothingAlong()
        {
            var vertical = MountBar();

            Assert.AreEqual(
                Thickness,
                vertical.GetIntrinsicWidth(Track),
                0.001f,
                "a vertical bar's natural width is its thickness"
            );
            Assert.AreEqual(
                0f,
                vertical.GetIntrinsicHeight(Thickness),
                0.001f,
                "a vertical bar has no natural height: it takes whatever it is given along the track"
            );

            var horizontal = MountBar(Axis.Horizontal);

            Assert.AreEqual(
                0f,
                horizontal.GetIntrinsicWidth(Thickness),
                0.001f,
                "and a horizontal bar has no natural width"
            );
            Assert.AreEqual(
                Thickness,
                horizontal.GetIntrinsicHeight(Track),
                0.001f,
                "a horizontal bar's natural height is its thickness"
            );
        }

        // -- Helpers -----------------------------------------------------------------------------

        private static float ExpectedThumbExtent => Track * Viewport / Content;

        private RenderScrollbar MountBar(Axis axis = Axis.Vertical)
        {
            var state = TestHarness.Mount(new Scrollbar { Controller = _controller, Axis = axis });
            _mounted.Add(state);
            return (RenderScrollbar)state.RenderObject;
        }

        private FakeScrollable Attach(
            float contentExtent,
            float viewportExtent,
            Axis axis = Axis.Vertical
        )
        {
            var fake = new FakeScrollable(_controller, contentExtent, viewportExtent, axis);
            _controller.Attach(fake);
            return fake;
        }
    }

    /// <summary>
    ///     A scrollable a test dictates the geometry of. Its offset is the controller's own, so a
    ///     <see cref="ScrollController.JumpTo" /> is visible in the metrics the way a real list's is.
    /// </summary>
    internal sealed class FakeScrollable : IScrollControllerExecutor
    {
        private sealed class OwnerState : FakeState { }

        private readonly ScrollController _controller;
        private readonly MutableAtom<ScrollMetrics> _shape;

        public FakeScrollable(
            ScrollController controller,
            float contentExtent,
            float viewportExtent,
            Axis axis = Axis.Vertical
        )
        {
            _controller = controller;
            _shape = Atom.Value(new ScrollMetrics(0f, contentExtent, viewportExtent, axis));
        }

        public IState Owner { get; } = new OwnerState();

        /// <summary>How many times the controller has asked this scrollable to stop and land.</summary>
        public int SnapCount { get; private set; }

        public ScrollMetrics? Metrics =>
            _shape.Value with
            {
                PixelOffset = _controller.PixelOffset,
            };

        /// <summary>Restates the content extent, as a lazy list's estimate does between passes.</summary>
        public void SetContentExtent(float contentExtent) =>
            _shape.Value = _shape.Value with { ContentExtent = contentExtent };

        public bool ScrollTo(
            int index,
            float duration,
            ScrollToPosition position,
            Easing? easing
        ) => false;

        public bool ScrollTo(Key key, float duration, ScrollToPosition position, Easing? easing) =>
            false;

        public void SnapToControllerOffset() => SnapCount++;
    }
}
