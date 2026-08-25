using System.Linq;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // A list that is re-inflated against a ScrollController which already remembers a deep offset
    // (a tab switch that comes back to a scrolled room) must cover the viewport on its very first
    // layout pass. The lazy window is selected from an average extent the fresh render object has
    // not measured yet, so this is where a guess that undershoots the real item sizes would leave
    // the top of the viewport unbuilt until the next pass.
    public class ScrollListRememberedOffsetTests
    {
        private const float Viewport = 700f;

        private static (State state, ScrollController controller) MountAt(float rememberedOffset)
        {
            var controller = new ScrollController(new LifetimeController().Lifetime)
            {
                PixelOffset = rememberedOffset,
            };

            var widget = new ScrollList
            {
                ScrollController = controller,
                ItemCount = 400,
                ItemBuilder = (context, index) =>
                    new FixedSizeBox
                    {
                        Key = Key.Of(index),
                        // Every sixth row is a short group header, the rest are taller than the
                        // render object's untrained default estimate.
                        Size = new Vector2(800, index % 6 == 0 ? 60 : 110),
                    },
            };

            return (TestHarness.Mount(widget), controller);
        }

        private static (float firstTop, float lastBottom, int count) VisibleRun(State state)
        {
            var layout = ((IMultiChildrenRenderObject)state.RenderObject).ChildrenLayout;
            return (
                layout.Min(l => l.Position.y),
                layout.Max(l => l.Position.y + l.Size.y),
                layout.Count
            );
        }

        [TestCase(1500f)]
        [TestCase(5000f)]
        [TestCase(15000f)]
        [TestCase(30000f)]
        public void FreshList_AtRememberedOffset_CoversTheViewportOnTheFirstPass(float offset)
        {
            var (state, controller) = MountAt(offset);

            TestHarness.Layout(state, LayoutConstraints.Tight(800, Viewport));
            var first = VisibleRun(state);

            controller.PixelOffset = offset + 1f;
            TestHarness.Layout(state, LayoutConstraints.Tight(800, Viewport));
            var second = VisibleRun(state);

            TestContext.WriteLine(
                $"offset {offset}: first pass covers [{first.firstTop}, {first.lastBottom}) with {first.count} rows; "
                    + $"after a 1px nudge [{second.firstTop}, {second.lastBottom}) with {second.count} rows"
            );

            Assert.LessOrEqual(
                first.firstTop,
                offset,
                $"first pass at {offset}: the first built row starts {first.firstTop - offset}px below the viewport top, leaving a gap"
            );
            Assert.GreaterOrEqual(
                first.lastBottom,
                offset + Viewport,
                $"first pass at {offset}: the last built row ends above the viewport bottom"
            );
        }
    }
}
