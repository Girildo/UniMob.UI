using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // Covers RenderObject's own base-class contract, independent of any specific subclass -- notably
    // not otherwise exercised directly anywhere else (individual RenderObjects mostly assume they're
    // never called with a disposed lifetime in the first place).
    public class RenderObjectTests
    {
        private class CountingRenderObject : RenderObject
        {
            public int SizingCalls;
            public int PositioningCalls;

            public CountingRenderObject(Lifetime lifetime) : base(lifetime)
            {
            }

            protected override Vector2 PerformSizing(LayoutConstraints constraints)
            {
                SizingCalls++;
                return new Vector2(1, 1);
            }

            protected override void PerformPositioning() => PositioningCalls++;
            protected override float ComputeIntrinsicWidth(float height) => 0f;
            protected override float ComputeIntrinsicHeight(float width) => 0f;
        }

        [Test]
        public void PerformLayoutImmediate_SkipsSizingAndPositioning_WhenLifetimeIsDisposed()
        {
            var controller = new LifetimeController();
            controller.Dispose();

            var renderObject = new CountingRenderObject(controller.Lifetime);
            renderObject.PerformLayoutImmediate(LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(0, renderObject.SizingCalls);
            Assert.AreEqual(0, renderObject.PositioningCalls);
            Assert.AreEqual(Vector2.zero, renderObject.Size);
        }

        [Test]
        public void PerformLayoutImmediate_RunsSizingThenPositioning_WhenLifetimeIsAlive()
        {
            var renderObject = new CountingRenderObject(Lifetime.Eternal);
            renderObject.PerformLayoutImmediate(LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(1, renderObject.SizingCalls);
            Assert.AreEqual(1, renderObject.PositioningCalls);
            Assert.AreEqual(new Vector2(1, 1), renderObject.Size);
        }
    }
}
