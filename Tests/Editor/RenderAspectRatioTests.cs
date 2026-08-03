using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class RenderAspectRatioTests
    {
        private class FakeAspectRatioState : FakeSingleChildLayoutState, IAspectRatioState
        {
            public float AspectRatio { get; set; } = 1f;
        }

        private static RenderAspectRatio Build(float aspectRatio, IState child = null)
        {
            return new RenderAspectRatio(new FakeAspectRatioState { AspectRatio = aspectRatio, Child = child });
        }

        [Test]
        public void TightConstraints_EscapeHatch_IgnoresAspectRatio()
        {
            var render = Build(aspectRatio: 2f);
            render.Layout(LayoutConstraints.Tight(80, 60));

            Assert.AreEqual(new Vector2(80, 60), render.PeekSize());
        }

        [Test]
        public void BoundedWidth_MaximizesWidth_ThenDerivesHeight()
        {
            var render = Build(aspectRatio: 2f);
            render.Layout(LayoutConstraints.Loose(100, 1000));

            Assert.AreEqual(new Vector2(100, 50), render.PeekSize());
        }

        [Test]
        public void UnboundedWidth_MaximizesHeight_ThenDerivesWidth()
        {
            var render = Build(aspectRatio: 2f);
            render.Layout(new LayoutConstraints(0, 0, float.PositiveInfinity, 60));

            Assert.AreEqual(new Vector2(120, 60), render.PeekSize());
        }

        [Test]
        public void MaxHeightOverflow_RescalesBothAxes_PreservingAspectRatio()
        {
            var render = Build(aspectRatio: 2f);
            render.Layout(new LayoutConstraints(0, 0, 100, 30));

            // Naive width=100 => height=50 overflows MaxHeight=30, so height is clamped to 30 and width
            // rescaled from it (30*2=60) instead of just being clamped independently -- this is what
            // keeps the aspect ratio (60/30=2) instead of distorting it.
            Assert.AreEqual(new Vector2(60, 30), render.PeekSize());
        }

        [Test]
        public void MinWidthOverflow_FinalConstrainActsAsSafetyNet_EvenIfRatioIsNotPreserved()
        {
            // Documents the fixed-order clamping: correcting for MinWidth here recomputes height (300)
            // without re-checking it against MaxHeight (40) -- the final outer Constrain() is what
            // actually prevents the out-of-bounds result, even though the aspect ratio itself isn't
            // preserved in this doubly-constrained scenario.
            var render = Build(aspectRatio: 0.1f);
            render.Layout(new LayoutConstraints(30, 0, float.PositiveInfinity, 40));

            Assert.AreEqual(new Vector2(30, 40), render.PeekSize());
        }

        [Test]
        public void ChildIsForcedToTheComputedSize()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(999, 999) });
            var render = Build(aspectRatio: 2f, child);
            render.Layout(LayoutConstraints.Loose(100, 1000));

            Assert.AreEqual(new Vector2(100, 50), render.ChildSize);
        }

        [Test]
        public void IntrinsicWidth_IsZero_WhenHeightIsInfinite()
        {
            var render = Build(aspectRatio: 2f);
            Assert.AreEqual(0f, render.GetIntrinsicWidth(float.PositiveInfinity));
        }

        [Test]
        public void IntrinsicHeight_IsZero_WhenWidthIsInfinite()
        {
            var render = Build(aspectRatio: 2f);
            Assert.AreEqual(0f, render.GetIntrinsicHeight(float.PositiveInfinity));
        }

        [Test]
        public void IntrinsicSize_DerivesFromAspectRatio()
        {
            var render = Build(aspectRatio: 2f);

            Assert.AreEqual(20f, render.GetIntrinsicWidth(10f));
            Assert.AreEqual(5f, render.GetIntrinsicHeight(10f));
        }
    }
}
