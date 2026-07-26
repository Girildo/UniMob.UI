using NUnit.Framework;
using UniMob.UI.Layout;
using UnityEngine;

namespace UniMob.UI.Tests
{
    public class CompositeTransitionTests
    {
        [Test]
        public void WithChild_SizesToChild_LikeAPassThroughProxy()
        {
            var size = TestHarness.MountAndLayout(
                new CompositeTransition { Child = new FixedSizeBox { Size = new Vector2(30, 40) } },
                LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(new Vector2(30, 40), size);
        }

        [Test]
        public void TransformProperties_DoNotAffectLayoutSize()
        {
            // Opacity/scale/position/rotation are applied at the view level only; layout stays the child's size.
            var size = TestHarness.MountAndLayout(
                new CompositeTransition
                {
                    Child = new FixedSizeBox { Size = new Vector2(30, 40) },
                    Opacity = new ConstAnimation<float>(0.5f),
                    Scale = new ConstAnimation<Vector3>(new Vector3(2f, 2f, 1f)),
                    Position = new ConstAnimation<Vector2>(new Vector2(0.5f, 0.5f)),
                    Rotation = new ConstAnimation<Quaternion>(Quaternion.Euler(0f, 0f, 45f)),
                },
                LayoutConstraints.Loose(100, 100));

            Assert.AreEqual(new Vector2(30, 40), size);
        }

        [Test]
        public void WithoutChild_CollapsesToSmallestAllowedByConstraints()
        {
            var size = TestHarness.MountAndLayout(
                new CompositeTransition(),
                new LayoutConstraints(10, 20, 100, 100));

            Assert.AreEqual(new Vector2(10, 20), size);
        }
    }
}
