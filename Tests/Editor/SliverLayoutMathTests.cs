using NUnit.Framework;
using UniMob.UI.Rendering;

namespace UniMob.UI.Tests
{
    // Covers the Nearest arm of SliverLayoutMath.AlignToScrollPosition, the offset both sliver render
    // objects answer for ScrollToPosition.Nearest before clamping it to the scrollable range.
    public class SliverLayoutMathTests
    {
        private const float Viewport = 100f;

        [Test]
        public void Nearest_AChildFullyInView_KeepsTheCurrentOffset()
        {
            Assert.AreEqual(
                40f,
                Nearest(leadingEdge: 60f, childSize: 30f, currentOffset: 40f),
                0.01f,
                "a child spanning [60, 90) is already inside the viewport [40, 140)"
            );
        }

        [Test]
        public void Nearest_AChildEndingOnTheViewportsTrailingEdge_KeepsTheCurrentOffset()
        {
            Assert.AreEqual(
                0f,
                Nearest(leadingEdge: 70f, childSize: 30f, currentOffset: 0f),
                0.01f,
                "a child spanning [70, 100) ends exactly where the viewport [0, 100) does"
            );
        }

        [Test]
        public void Nearest_AChildPastTheTrailingEdge_EndsAtTheBottomOfTheViewport()
        {
            Assert.AreEqual(
                20f,
                Nearest(leadingEdge: 90f, childSize: 30f, currentOffset: 0f),
                0.01f,
                "a child spanning [90, 120) should end at the bottom, which needs the viewport at [20, 120)"
            );
        }

        [Test]
        public void Nearest_AChildBeforeTheLeadingEdge_StartsAtTheTopOfTheViewport()
        {
            Assert.AreEqual(
                10f,
                Nearest(leadingEdge: 10f, childSize: 30f, currentOffset: 50f),
                0.01f,
                "a child spanning [10, 40) above the viewport [50, 150) should start at the top"
            );
        }

        [Test]
        public void Nearest_AChildLongerThanTheViewport_StartsAtTheTopOfTheViewport()
        {
            Assert.AreEqual(
                50f,
                Nearest(leadingEdge: 50f, childSize: 150f, currentOffset: 0f),
                0.01f,
                "a child that cannot fit shows its leading edge rather than its trailing one"
            );
        }

        private static float Nearest(float leadingEdge, float childSize, float currentOffset) =>
            SliverLayoutMath.AlignToScrollPosition(
                leadingEdge,
                childSize,
                Viewport,
                currentOffset,
                ScrollToPosition.Nearest
            );
    }
}
