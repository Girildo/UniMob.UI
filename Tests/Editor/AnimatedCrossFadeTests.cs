using NUnit.Framework;
using UniMob.UI.Layout;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // Duration = 0 keeps the AnimationController at rest at its terminal value on mount (no ticker, no Zone),
    // so the composed ZStack is deterministic: the faded-out layer is unmounted (unless KeepMounted).
    public class AnimatedCrossFadeTests
    {
        private static readonly LayoutConstraints Loose = LayoutConstraints.Loose(1000, 1000);

        private static AnimatedCrossFade Make(CrossFadeState state, bool keepMounted = false) =>
            new AnimatedCrossFade
            {
                CrossFadeState = state,
                Duration = 0f,
                KeepMounted = keepMounted,
                FirstChild = new FixedSizeBox { Size = new Vector2(30, 40) },
                SecondChild = new FixedSizeBox { Size = new Vector2(80, 90) },
            };

        [Test]
        public void ShowFirst_UnmountsSecond_SizesToFirstChild()
        {
            var size = TestHarness.MountAndLayout(Make(CrossFadeState.ShowFirst), Loose);

            Assert.AreEqual(new Vector2(30, 40), size);
        }

        [Test]
        public void ShowSecond_UnmountsFirst_SizesToSecondChild()
        {
            var size = TestHarness.MountAndLayout(Make(CrossFadeState.ShowSecond), Loose);

            Assert.AreEqual(new Vector2(80, 90), size);
        }

        [Test]
        public void KeepMounted_KeepsBothLayers_SizesToLargerChild()
        {
            // Both children stay mounted; the ZStack sizes to the max extent of both.
            var size = TestHarness.MountAndLayout(
                Make(CrossFadeState.ShowFirst, keepMounted: true),
                Loose
            );

            Assert.AreEqual(new Vector2(80, 90), size);
        }
    }
}
