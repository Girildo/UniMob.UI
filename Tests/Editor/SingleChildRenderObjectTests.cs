using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class SingleChildRenderObjectTests
    {
        // SingleChildRenderObject itself is abstract (PerformSizing/PerformPositioning aren't
        // implemented) -- this is the minimal concrete shape needed to exercise its own
        // ComputeIntrinsicWidth/Height forwarding, which every subclass inherits unchanged.
        private class MinimalSingleChildRenderObject : SingleChildRenderObject
        {
            public MinimalSingleChildRenderObject(ISingleChildLayoutState state) : base(state)
            {
            }

            protected override Vector2 PerformSizing(LayoutConstraints constraints) => Vector2.zero;
            protected override void PerformPositioning()
            {
            }
        }

        [Test]
        public void ComputeIntrinsicSize_ForwardsToChild()
        {
            var child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(30, 40) });
            var state = new FakeSingleChildLayoutState { Child = child };

            var renderObject = new MinimalSingleChildRenderObject(state);

            Assert.AreEqual(30f, renderObject.GetIntrinsicWidth(100f));
            Assert.AreEqual(40f, renderObject.GetIntrinsicHeight(100f));
        }

        [Test]
        public void ComputeIntrinsicSize_IsZero_WithNoChild()
        {
            var state = new FakeSingleChildLayoutState { Child = null };

            var renderObject = new MinimalSingleChildRenderObject(state);

            Assert.AreEqual(0f, renderObject.GetIntrinsicWidth(100f));
            Assert.AreEqual(0f, renderObject.GetIntrinsicHeight(100f));
        }
    }
}
