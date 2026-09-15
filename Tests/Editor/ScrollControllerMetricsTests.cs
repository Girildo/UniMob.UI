using System.Collections.Generic;
using NUnit.Framework;
using UniMob.Core;
using UniMob.UI;
using UniMob.UI.Internal;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     <see cref="ScrollController.Metrics"/> is the scrollable's geometry read from outside the
    ///     scrollable: a scrollbar, a "scroll to top" button and a progress indicator all depend on it
    ///     before the list they describe has been mounted, let alone laid out.
    /// </summary>
    /// <remarks>
    ///     Two things are pinned here that a plain property would get wrong. The binding is observable,
    ///     so an observer created before anything attaches ends up depending on the list that attaches
    ///     later, and wakes when that list is laid out and again when it goes away, rather than sleeping
    ///     on an empty dependency list. And the metrics are recomputed on every layout pass rather than
    ///     only on a size change, because a lazy list's content extent is an estimate that refines under
    ///     a viewport that never moves.
    /// </remarks>
    public class ScrollControllerMetricsTests
    {
        private const float ViewportWidth = 300f;
        private const float ViewportHeight = 400f;
        private const float ItemExtent = 50f;
        private const float Spacing = 10f;

        private LifetimeController lifetime = null!;

        [SetUp]
        public void SetUp()
        {
            this.lifetime = new LifetimeController();
        }

        [TearDown]
        public void TearDown()
        {
            this.lifetime.Dispose();
        }

        // -- Binding -------------------------------------------------------------------------------

        [Test]
        public void AnObserverCreatedBeforeAnyList_WakesOnLayout_AndOnDetach()
        {
            using var errors = RecordingErrors.Capture();

            var controller = this.NewController();
            var runs = 0;
            ScrollMetrics? seen = null;

            // The Action overload, not the value-diffing one: the projection stays null from creation
            // through attach, so an equality cutoff could not tell "woke and saw nothing" apart from
            // "never woke at all" -- which is the whole claim under test.
            Atom.Reaction(
                this.lifetime.Lifetime,
                () =>
                {
                    runs++;
                    seen = controller.Metrics;
                }
            );

            Assert.AreEqual(1, runs, "a reaction runs once when it is created");
            Assert.IsNull(
                seen,
                "nothing is attached, so there is no scrollable whose geometry could be reported"
            );

            var state = TestHarness.Mount(LazyList(controller, itemCount: 20));
            AtomScheduler.Sync();
            var runsBeforeLayout = runs;

            Assert.IsNull(
                seen,
                "a mounted but never laid out list has no geometry yet, and must report null rather "
                    + "than a zero that reads like a measurement"
            );
            Assert.IsEmpty(
                errors,
                "reading the metrics of a never laid out list must not report a fault"
            );
            LogAssert.NoUnexpectedReceived();

            TestHarness.Layout(state, LayoutConstraints.Tight(ViewportWidth, ViewportHeight));
            AtomScheduler.Sync();

            Assert.Greater(
                runs,
                runsBeforeLayout,
                "the first layout pass must wake an observer created before the list existed; had the "
                    + "attachment not been observable, that observer would have registered no dependency "
                    + "on the list at all and slept through this pass"
            );
            Assert.IsNotNull(seen, "a laid out list reports its geometry");
            Assert.AreEqual(
                ViewportHeight,
                seen!.Value.ViewportExtent,
                0.01f,
                "the viewport extent is the list's own main-axis size, as pushed by its parent"
            );
            Assert.AreEqual(
                LazyContentExtent(20),
                seen.Value.ContentExtent,
                0.01f,
                "the content extent is the total main-axis size of all 20 items plus the gaps between them"
            );
            Assert.AreEqual(
                0f,
                seen.Value.PixelOffset,
                0.01f,
                "an untouched controller sits at the start of the content"
            );

            var runsBeforeDeactivation = runs;

            StateUtilities.DeactivateChild(state);
            AtomScheduler.Sync();

            Assert.Greater(
                runs,
                runsBeforeDeactivation,
                "detaching must be observed the same way the layout was: a scrollbar has to stop "
                    + "drawing itself when the list it describes goes away"
            );
            Assert.IsNull(
                seen,
                "a deactivated list has no geometry, and leaving the last measurement behind would "
                    + "report a viewport that no longer exists"
            );
        }

        [Test]
        public void AttachingAnotherList_RebindsTheMetricsToIt()
        {
            var controller = this.NewController();

            var first = TestHarness.Mount(LazyList(controller, itemCount: 20));
            TestHarness.Layout(first, LayoutConstraints.Tight(ViewportWidth, ViewportHeight));

            Assert.AreEqual(
                LazyContentExtent(20),
                controller.Metrics!.Value.ContentExtent,
                0.01f,
                "the first list is the one attached, so its content is what the controller reports"
            );

            StateUtilities.DeactivateChild(first);

            Assert.IsFalse(
                controller.IsAttached,
                "deactivating the only attached list leaves the controller with nothing to drive"
            );

            var second = TestHarness.Mount(LazyList(controller, itemCount: 7));
            TestHarness.Layout(second, LayoutConstraints.Tight(ViewportWidth, 120f));

            Assert.IsTrue(controller.IsAttached, "the replacement list attaches on mount");
            Assert.AreEqual(
                LazyContentExtent(7),
                controller.Metrics!.Value.ContentExtent,
                0.01f,
                "the controller must report the list it is now attached to, not the one it used to drive"
            );
            Assert.AreEqual(
                120f,
                controller.Metrics!.Value.ViewportExtent,
                0.01f,
                "the replacement list's own viewport, not the viewport of the list it replaced"
            );
        }

        // -- What the numbers mean -----------------------------------------------------------------

        [Test]
        public void LazyList_WithAFixedItemExtent_ReportsExactContent()
        {
            var controller = this.NewController();
            var state = TestHarness.Mount(LazyList(controller, itemCount: 20));

            TestHarness.Layout(state, LayoutConstraints.Tight(ViewportWidth, ViewportHeight));

            var metrics = controller.Metrics!.Value;

            Assert.AreEqual(
                20 * ItemExtent + 19 * Spacing,
                metrics.ContentExtent,
                0.01f,
                "20 items of a known extent with 19 gaps between them is an exact total, not an estimate"
            );
            Assert.AreEqual(
                TotalContentSizeOf(state),
                metrics.ContentExtent,
                0.01f,
                "the metrics must report the same content size the view sizes its content rect to; "
                    + "a second opinion here is a scrollbar that disagrees with the ScrollRect"
            );
            Assert.AreEqual(
                metrics.ContentExtent - ViewportHeight,
                metrics.MaxScrollExtent,
                0.01f,
                "the furthest the content can travel is everything that does not fit the viewport"
            );
            Assert.AreEqual(
                Axis.Vertical,
                metrics.Axis,
                "a vertical list scrolls vertically, and a scrollbar reads the axis to know where to draw"
            );
        }

        [Test]
        public void LazyList_GrowingItemCount_GrowsTheContentExtent()
        {
            var controller = this.NewController();
            var widget = LazyList(controller, itemCount: 20);
            var state = TestHarness.Mount(widget);

            TestHarness.Layout(state, LayoutConstraints.Tight(ViewportWidth, ViewportHeight));

            Assert.AreEqual(
                LazyContentExtent(20),
                controller.Metrics!.Value.ContentExtent,
                0.01f,
                "20 items before the update"
            );

            var updated = TestHarness.Update(state, LazyList(controller, itemCount: 50));
            TestHarness.Layout(updated, LayoutConstraints.Tight(ViewportWidth, ViewportHeight));

            Assert.AreEqual(
                LazyContentExtent(50),
                controller.Metrics!.Value.ContentExtent,
                0.01f,
                "items added to the list must grow the reported content; a metric that only refreshes "
                    + "when the viewport resizes would leave a scrollbar sized for the old count"
            );
        }

        [Test]
        public void TheReportedOffset_IsTheControllersOwn()
        {
            var controller = this.NewController();
            var state = TestHarness.Mount(LazyList(controller, itemCount: 20));

            TestHarness.Layout(state, LayoutConstraints.Tight(ViewportWidth, ViewportHeight));

            controller.PixelOffset = 275f;
            TestHarness.Layout(state, LayoutConstraints.Tight(ViewportWidth, ViewportHeight));

            Assert.AreEqual(
                275f,
                controller.Metrics!.Value.PixelOffset,
                0.01f,
                "the offset in the metrics is the controller's own, so everything reading the metrics "
                    + "and everything reading PixelOffset agree on where the list is"
            );
        }

        [Test]
        public void RelayoutUnderADifferentViewport_ReportsTheNewViewport()
        {
            var controller = this.NewController();
            var state = TestHarness.Mount(LazyList(controller, itemCount: 20));

            TestHarness.Layout(state, LayoutConstraints.Tight(ViewportWidth, ViewportHeight));
            TestHarness.Layout(state, LayoutConstraints.Tight(ViewportWidth, 180f));

            var metrics = controller.Metrics!.Value;

            Assert.AreEqual(
                180f,
                metrics.ViewportExtent,
                0.01f,
                "a resized viewport must be reported; a scrollbar thumb is sized from this ratio"
            );
            Assert.AreEqual(
                LazyContentExtent(20) - 180f,
                metrics.MaxScrollExtent,
                0.01f,
                "a smaller viewport leaves more of the content to scroll through"
            );
        }

        [Test]
        public void EagerList_ReportsTheSumOfItsChildren()
        {
            const float childExtent = 70f;
            const int childCount = 5;

            var controller = this.NewController();
            var children = new List<Widget>();
            for (var i = 0; i < childCount; i++)
            {
                children.Add(
                    new FixedSizeBox
                    {
                        Key = Key.Of(i),
                        Size = new Vector2(ViewportWidth, childExtent),
                    }
                );
            }

            var state = TestHarness.Mount(
                new ScrollList
                {
                    ScrollController = controller,
                    Spacing = Spacing,
                    Children = children,
                }
            );

            TestHarness.Layout(state, LayoutConstraints.Tight(ViewportWidth, ViewportHeight));

            var metrics = controller.Metrics!.Value;

            Assert.AreEqual(
                childCount * childExtent + (childCount - 1) * Spacing,
                metrics.ContentExtent,
                0.01f,
                "an eagerly built list measures every child, so its content is their exact sum plus the gaps"
            );
            Assert.AreEqual(
                TotalContentSizeOf(state),
                metrics.ContentExtent,
                0.01f,
                "the metrics and the render object must not disagree about the content size"
            );
            Assert.IsFalse(
                metrics.CanScroll,
                "390px of content in a 400px viewport fits, and a list that fits does not scroll"
            );
        }

        [Test]
        public void Grid_ContentExtent_IncludesTheMainAxisPadding()
        {
            const float mainExtent = 50f;
            const float mainSpacing = 6f;
            const float startPadding = 12f;
            const float endPadding = 20f;
            const int itemCount = 8;
            const int crossAxisCount = 2;
            const int rowCount = itemCount / crossAxisCount;

            var controller = this.NewController();
            var children = new List<Widget>();
            for (var i = 0; i < itemCount; i++)
            {
                children.Add(
                    new FixedSizeBox { Key = Key.Of(i), Size = new Vector2(100, mainExtent) }
                );
            }

            var state = TestHarness.Mount(
                new ScrollGrid
                {
                    ScrollController = controller,
                    CrossAxisCount = crossAxisCount,
                    MainAxisExtent = mainExtent,
                    MainAxisSpacing = mainSpacing,
                    Padding = RectPadding.FromLTRB(0, startPadding, 0, endPadding),
                    Children = children,
                }
            );

            TestHarness.Layout(state, LayoutConstraints.Tight(ViewportWidth, ViewportHeight));

            var metrics = controller.Metrics!.Value;

            Assert.AreEqual(
                startPadding + rowCount * (mainExtent + mainSpacing) - mainSpacing + endPadding,
                metrics.ContentExtent,
                0.01f,
                "the padding at both ends of the scroll axis is scrollable content: a grid whose metrics "
                    + "omitted it would stop short of its own last row"
            );
            Assert.AreEqual(
                TotalContentSizeOf(state),
                metrics.ContentExtent,
                0.01f,
                "the grid's metrics must report the same content size it lays its rows out within"
            );
        }

        [Test]
        public void HorizontalList_MeasuresAlongTheWidth()
        {
            var controller = this.NewController();
            var state = TestHarness.Mount(
                LazyList(controller, itemCount: 20, axis: Axis.Horizontal)
            );

            TestHarness.Layout(state, LayoutConstraints.Tight(ViewportWidth, ViewportHeight));

            var metrics = controller.Metrics!.Value;

            Assert.AreEqual(
                Axis.Horizontal,
                metrics.Axis,
                "the metrics carry the axis so a reader knows which of the two extents it is holding"
            );
            Assert.AreEqual(
                ViewportWidth,
                metrics.ViewportExtent,
                0.01f,
                "a horizontal list's viewport is its width; reporting the height would size a horizontal "
                    + "scrollbar from the wrong axis"
            );
            Assert.AreEqual(
                LazyContentExtent(20),
                metrics.ContentExtent,
                0.01f,
                "the content of a horizontal list runs along its width"
            );
        }

        // -- NormalizedValue -----------------------------------------------------------------------

        [Test]
        public void NormalizedValue_IsZeroAtTheStart_AndOneAtTheEnd()
        {
            var controller = this.NewController();
            var state = TestHarness.Mount(LazyList(controller, itemCount: 20));

            TestHarness.Layout(state, LayoutConstraints.Tight(ViewportWidth, ViewportHeight));
            var maxScrollExtent = controller.Metrics!.Value.MaxScrollExtent;

            Assert.AreEqual(
                0f,
                controller.NormalizedValue,
                0.0001f,
                "an untouched list sits at the start of its range"
            );

            controller.PixelOffset = maxScrollExtent;
            Assert.AreEqual(
                1f,
                controller.NormalizedValue,
                0.0001f,
                "the maximum scroll extent is the end of the range, whatever the content size is"
            );

            controller.PixelOffset = maxScrollExtent / 2f;
            Assert.AreEqual(
                0.5f,
                controller.NormalizedValue,
                0.0001f,
                "halfway through the scrollable range reads as half"
            );
        }

        [Test]
        public void NormalizedValue_ClampsOverscroll()
        {
            var controller = this.NewController();
            var state = TestHarness.Mount(LazyList(controller, itemCount: 20));

            TestHarness.Layout(state, LayoutConstraints.Tight(ViewportWidth, ViewportHeight));
            var maxScrollExtent = controller.Metrics!.Value.MaxScrollExtent;

            controller.PixelOffset = -120f;
            Assert.AreEqual(
                0f,
                controller.NormalizedValue,
                0.0001f,
                "an Elastic list dragged past its start overscrolls below zero, and a ratio must not "
                    + "follow it there"
            );

            controller.PixelOffset = maxScrollExtent + 120f;
            Assert.AreEqual(
                1f,
                controller.NormalizedValue,
                0.0001f,
                "the same at the far end: a bouncing list must not report more than a full range"
            );
        }

        [Test]
        public void NormalizedValue_IsZero_WhenThereIsNothingToScroll()
        {
            var controller = this.NewController();

            Assert.AreEqual(
                0f,
                controller.NormalizedValue,
                0.0001f,
                "an unattached controller has no range, and must answer zero rather than divide by one"
            );

            var state = TestHarness.Mount(LazyList(controller, itemCount: 2));

            Assert.AreEqual(
                0f,
                controller.NormalizedValue,
                0.0001f,
                "a list that has not been laid out has no range yet"
            );

            TestHarness.Layout(state, LayoutConstraints.Tight(ViewportWidth, ViewportHeight));

            Assert.IsFalse(
                controller.Metrics!.Value.CanScroll,
                "two items fit the viewport, so this is the content-fits case"
            );
            Assert.AreEqual(
                0f,
                controller.NormalizedValue,
                0.0001f,
                "content that fits leaves nothing to express as a ratio"
            );
        }

        // -- JumpTo --------------------------------------------------------------------------------

        [Test]
        public void JumpTo_WithNothingAttached_DoesNothing()
        {
            var controller = this.NewController();

            Assert.IsFalse(
                controller.JumpTo(500f),
                "a jump nobody can execute must say so rather than silently succeed"
            );
            Assert.AreEqual(
                0f,
                controller.PixelOffset,
                0.01f,
                "a refused jump must not move the controller, or the next list to attach would open "
                    + "at an offset the caller was told did not take"
            );
        }

        [Test]
        public void JumpTo_OnALaidOutList_WritesTheOffset_AndClampsToTheRange()
        {
            var controller = this.NewController();
            var state = TestHarness.Mount(LazyList(controller, itemCount: 20));

            TestHarness.Layout(state, LayoutConstraints.Tight(ViewportWidth, ViewportHeight));
            var maxScrollExtent = controller.Metrics!.Value.MaxScrollExtent;

            Assert.IsTrue(controller.JumpTo(275f), "an attached list can execute a jump");
            Assert.AreEqual(
                275f,
                controller.PixelOffset,
                0.01f,
                "the controller is the one place the offset is written for a jump; the list follows it"
            );

            controller.JumpTo(-40f);
            Assert.AreEqual(
                0f,
                controller.PixelOffset,
                0.01f,
                "a jump before the start of the content lands on the start; overscroll is a gesture, "
                    + "not a destination"
            );

            controller.JumpTo(maxScrollExtent + 10000f);
            Assert.AreEqual(
                maxScrollExtent,
                controller.PixelOffset,
                0.01f,
                "a jump past the end lands on the end, whatever the caller asked for"
            );
        }

        [Test]
        public void JumpTo_BeforeLayout_KnowsOnlyTheLowerBound()
        {
            var controller = this.NewController();
            TestHarness.Mount(LazyList(controller, itemCount: 20));

            Assert.IsNull(
                controller.Metrics,
                "the list is mounted but not laid out, so there is no range to clamp against"
            );

            Assert.IsTrue(
                controller.JumpTo(9000f),
                "a mounted list can take a jump before it has been laid out"
            );
            Assert.AreEqual(
                9000f,
                controller.PixelOffset,
                0.01f,
                "with no known content size the offset is kept as asked; clamping it to a range nobody "
                    + "has measured would throw away a remembered position"
            );

            controller.JumpTo(-1f);
            Assert.AreEqual(
                0f,
                controller.PixelOffset,
                0.01f,
                "the start of the content is known without measuring anything"
            );
        }

        // -- Double attach -------------------------------------------------------------------------

        [Test]
        public void TwoLivingListsOnOneController_AreReportedOnce()
        {
            using var zone = TestZone.Install();

            var controller = this.NewController();
            TestHarness.Mount(LazyList(controller, itemCount: 20));
            var second = TestHarness.Mount(LazyList(controller, itemCount: 7));

            zone.Pump();

            Assert.AreEqual(
                1,
                zone.Faults.Count,
                "two live scrollables sharing one controller is a wiring mistake, and must be reported "
                    + "exactly once: the first list silently stops responding to the controller"
            );
            Assert.AreSame(
                second,
                zone.Faults[0].Subject,
                "the report is about the attachment that took over, which is the one the author just wrote"
            );
            StringAssert.Contains(
                "20 items",
                zone.Faults[0].Exception.Message,
                "the message must name the list that was displaced, or there is nothing to go and find"
            );
            StringAssert.Contains(
                "7 items",
                zone.Faults[0].Exception.Message,
                "and the list that displaced it"
            );
        }

        [Test]
        public void ReplacingAListWithADifferentlyKeyedOne_IsNotADoubleAttach()
        {
            using var zone = TestZone.Install();

            var controller = this.NewController();
            var root = TestHarness.Mount(ColumnWith(controller, Key.Of("first")));

            Assert.AreEqual(
                1,
                ((IMultiChildLayoutState)root).Children.Length,
                "the column holds the one list under test"
            );

            TestHarness.Update(root, ColumnWith(controller, Key.Of("second")));
            _ = ((IMultiChildLayoutState)root).Children;

            zone.Pump();

            Assert.IsEmpty(
                zone.Faults,
                "a keyed reorder inflates the replacement before deactivating the list it replaces, so "
                    + "both are attached for an instant; reporting that would fire on every such swap"
            );
            Assert.IsTrue(
                controller.IsAttached,
                "the replacement keeps the controller driving a list"
            );
        }

        // -- Helpers -------------------------------------------------------------------------------

        private ScrollController NewController() => new ScrollController(this.lifetime.Lifetime);

        private static float LazyContentExtent(int itemCount) =>
            itemCount * (ItemExtent + Spacing) - Spacing;

        private static float TotalContentSizeOf(State state) =>
            ((IScrollableRenderObject)state.RenderObject).TotalContentSize();

        private static ScrollList LazyList(
            ScrollController controller,
            int itemCount,
            Axis axis = Axis.Vertical,
            Key? key = null
        )
        {
            return new ScrollList
            {
                Key = key,
                ScrollController = controller,
                Axis = axis,
                ItemCount = itemCount,
                ItemExtent = ItemExtent,
                Spacing = Spacing,
                ItemBuilder = (context, index) =>
                    new FixedSizeBox
                    {
                        Key = Key.Of(index),
                        Size =
                            axis == Axis.Horizontal
                                ? new Vector2(ItemExtent, ViewportHeight)
                                : new Vector2(ViewportWidth, ItemExtent),
                    },
            };
        }

        private static Column ColumnWith(ScrollController controller, Key key) =>
            new Column { Children = { LazyList(controller, itemCount: 20, key: key) } };
    }
}
