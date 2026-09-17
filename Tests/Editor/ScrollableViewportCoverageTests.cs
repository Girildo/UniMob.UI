using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // The invariant a virtualized scrollable owes its user: at every scroll offset, the children it
    // places cover the viewport. The extents below are not stationary (a run of tall slots followed
    // by a run of short ones, and the reverse), which is what separates an average-based guess of
    // where an index sits from where the measured slots actually put it. A slot is an item of the
    // list and a row of the grid.
    public class ScrollableViewportCoverageTests
    {
        private const float Viewport = 700f;
        private const float ScrollStep = 150f;
        private const float Spacing = 8f;
        private const int SlotCount = 300;
        private const int GridColumns = 3;

        private static float TallThenShort(int slot) => slot < 40 ? 300f : 40f;

        private static float ShortThenTall(int slot) => slot < 100 ? 40f : 300f;

        private static float TallSectionInTheMiddle(int slot) =>
            slot is >= 50 and < 80 ? 400f : 60f;

        private static float TinyAgainstTheDefaultEstimate(int slot) => 4f;

        private static readonly Dictionary<string, Func<int, float>> Profiles = new()
        {
            { nameof(TallThenShort), TallThenShort },
            { nameof(ShortThenTall), ShortThenTall },
            { nameof(TallSectionInTheMiddle), TallSectionInTheMiddle },
            { nameof(TinyAgainstTheDefaultEstimate), TinyAgainstTheDefaultEstimate },
        };

        private static IEnumerable<TestCaseData> Cases() =>
            from kind in new[] { "List", "Grid" }
            from profile in Profiles.Keys
            select new TestCaseData(kind, profile);

        private static (State state, ScrollController controller) Mount(
            string kind,
            Func<int, float> extentOf
        )
        {
            var controller = new ScrollController(new LifetimeController().Lifetime);

            Widget widget = kind switch
            {
                "List" => new ScrollList
                {
                    ScrollController = controller,
                    Spacing = Spacing,
                    ItemCount = SlotCount,
                    ItemBuilder = (context, index) =>
                        new FixedSizeBox
                        {
                            Key = Key.Of(index),
                            Size = new Vector2(800, extentOf(index)),
                        },
                },
                "Grid" => new ScrollGrid
                {
                    ScrollController = controller,
                    CrossAxisCount = GridColumns,
                    MainAxisSpacing = Spacing,
                    ItemCount = SlotCount * GridColumns,
                    ItemBuilder = (context, index) =>
                        new FixedSizeBox
                        {
                            Key = Key.Of(index),
                            Size = new Vector2(100, extentOf(index / GridColumns)),
                        },
                },
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };

            return (TestHarness.Mount(widget), controller);
        }

        private static float ExactContentSize(Func<int, float> extentOf) =>
            Enumerable.Range(0, SlotCount).Sum(extentOf) + Spacing * (SlotCount - 1);

        private static void AssertViewportCovered(State state, float requestedOffset, string step)
        {
            var renderObject = (IScrollableRenderObject)state.RenderObject;
            var layout = renderObject.ChildrenLayout;
            var contentSize = renderObject.TotalContentSize();

            // The scrollable shows the part of the offset its content can reach.
            var offset = Mathf.Min(requestedOffset, Mathf.Max(0f, contentSize - Viewport));
            var viewport =
                $"viewport [{offset}, {offset + Viewport}) of {contentSize}px of content";

            Assert.IsNotEmpty(layout, $"{step}: {viewport} holds no children at all");

            var firstTop = layout.Min(l => l.Position.y);
            var lastBottom = layout.Max(l => l.Position.y + l.Size.y);
            var placed = $"placed {layout.Count} children over [{firstTop}, {lastBottom})";

            Assert.LessOrEqual(
                firstTop,
                offset + 0.5f,
                $"{step}: {viewport} has a gap of {firstTop - offset}px at its top; {placed}"
            );
            Assert.GreaterOrEqual(
                lastBottom,
                Mathf.Min(offset + Viewport, contentSize) - 0.5f,
                $"{step}: {viewport} has a gap of {offset + Viewport - lastBottom}px at its bottom; {placed}"
            );
        }

        [TestCaseSource(nameof(Cases))]
        public void ScrollingDownThenBackUp_CoversTheViewportAtEveryStep(
            string kind,
            string profile
        )
        {
            var extentOf = Profiles[profile];
            var (state, controller) = Mount(kind, extentOf);
            var constraints = LayoutConstraints.Tight(800, Viewport);
            var lastOffset = Mathf.Max(0f, ExactContentSize(extentOf) - Viewport);

            TestHarness.Layout(state, constraints);
            AssertViewportCovered(state, 0f, "initial layout");

            for (var offset = ScrollStep; offset < lastOffset; offset += ScrollStep)
            {
                controller.PixelOffset = offset;
                TestHarness.Layout(state, constraints);
                AssertViewportCovered(state, offset, $"scrolling down to {offset}");
            }

            for (var offset = lastOffset; offset > 0f; offset -= ScrollStep)
            {
                controller.PixelOffset = offset;
                TestHarness.Layout(state, constraints);
                AssertViewportCovered(state, offset, $"scrolling back up to {offset}");
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void JumpingAcrossTheContent_CoversTheViewportOnTheFirstPass(
            string kind,
            string profile
        )
        {
            var extentOf = Profiles[profile];
            var (state, controller) = Mount(kind, extentOf);
            var constraints = LayoutConstraints.Tight(800, Viewport);
            var lastOffset = Mathf.Max(0f, ExactContentSize(extentOf) - Viewport);

            TestHarness.Layout(state, constraints);

            foreach (var fraction in new[] { 0.4f, 0.02f, 0.7f, 0.2f, 1f, 0.55f, 0f })
            {
                var offset = lastOffset * fraction;
                controller.PixelOffset = offset;
                TestHarness.Layout(state, constraints);
                AssertViewportCovered(state, offset, $"jumping to {offset}");
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void ExtentsChangingUnderADeepOffset_StillCoverTheViewport(
            string kind,
            string profile
        )
        {
            // A group collapsing above the viewport: every slot keeps its index but the measurements
            // taken for it are stale.
            var collapsed = false;
            var baseExtentOf = Profiles[profile];
            float ExtentOf(int slot) => collapsed && slot < 30 ? 10f : baseExtentOf(slot);

            var (state, controller) = Mount(kind, ExtentOf);
            var constraints = LayoutConstraints.Tight(800, Viewport);
            var lastOffset = Mathf.Max(0f, ExactContentSize(baseExtentOf) - Viewport);

            for (var offset = 0f; offset < lastOffset * 0.5f; offset += ScrollStep)
            {
                controller.PixelOffset = offset;
                TestHarness.Layout(state, constraints);
            }

            collapsed = true;
            for (var offset = lastOffset * 0.5f; offset > 0f; offset -= ScrollStep)
            {
                controller.PixelOffset = offset;
                TestHarness.Layout(state, constraints);
                AssertViewportCovered(
                    state,
                    offset,
                    $"after the collapse, scrolling up to {offset}"
                );
            }
        }
    }
}
