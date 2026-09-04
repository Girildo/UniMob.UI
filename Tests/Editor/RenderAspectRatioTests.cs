using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
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
            return new RenderAspectRatio(
                new FakeAspectRatioState { AspectRatio = aspectRatio, Child = child }
            );
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
        public void IntrinsicWidth_IsZero_WhenHeightIsUnboundedAndThereIsNoChild()
        {
            var render = Build(aspectRatio: 2f);
            Assert.AreEqual(0f, render.GetIntrinsicWidth(float.PositiveInfinity));
        }

        [Test]
        public void IntrinsicHeight_IsZero_WhenWidthIsUnboundedAndThereIsNoChild()
        {
            var render = Build(aspectRatio: 2f);
            Assert.AreEqual(0f, render.GetIntrinsicHeight(float.PositiveInfinity));
        }

        [Test]
        public void IntrinsicWidth_FallsBackToTheChild_WhenHeightIsUnbounded()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(70, 40) });
            var render = Build(aspectRatio: 2f, child);

            // Not 0: with no height to scale, the ratio implies nothing and the child is the only
            // thing that knows a size. Zero would be indistinguishable from a measured zero.
            Assert.AreEqual(70f, render.GetIntrinsicWidth(float.PositiveInfinity));
        }

        [Test]
        public void IntrinsicHeight_FallsBackToTheChild_WhenWidthIsUnbounded()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(70, 40) });
            var render = Build(aspectRatio: 2f, child);

            Assert.AreEqual(40f, render.GetIntrinsicHeight(float.PositiveInfinity));
        }

        [Test]
        public void IntrinsicSize_DerivesFromAspectRatio()
        {
            var render = Build(aspectRatio: 2f);

            Assert.AreEqual(20f, render.GetIntrinsicWidth(10f));
            Assert.AreEqual(5f, render.GetIntrinsicHeight(10f));
        }

        [Test]
        public void IntrinsicSize_PrefersTheRatioOverTheChild_WhenTheArgumentIsBounded()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(70, 40) });
            var render = Build(aspectRatio: 2f, child);

            // The child is consulted only where the ratio has nothing to say. A bounded argument
            // determines the other axis outright, and the child is about to be forced to it anyway.
            Assert.AreEqual(20f, render.GetIntrinsicWidth(10f));
            Assert.AreEqual(5f, render.GetIntrinsicHeight(10f));
        }

        [Test]
        public void RatioIsRereadOnRebuild()
        {
            var constraints = LayoutConstraints.Loose(100, 1000);
            var state = TestHarness.Mount(new AspectRatio { Ratio = 2f });

            Assert.AreEqual(new Vector2(100, 50), TestHarness.Layout(state, constraints));

            TestHarness.Update(state, new AspectRatio { Ratio = 1f });

            // Same constraints, so only the widget swap can move the answer. A render object that
            // snapshots the ratio at construction stays at 2 here and reports (100, 50) forever.
            Assert.AreEqual(new Vector2(100, 100), TestHarness.Layout(state, constraints));
        }
    }
}
