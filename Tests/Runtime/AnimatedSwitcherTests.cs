using System.Collections.Generic;
using NUnit.Framework;
using UniMob.UI.Layout;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // Must run as a PlayMode test, not EditMode: AnimatedSwitcher drives AnimationControllers, and
    // every start/stop goes through Zone.Current, which is only created by a RuntimeInitializeOnLoad
    // hook that EditMode never runs.
    public class AnimatedSwitcherTests
    {
        private static readonly LayoutConstraints Loose = LayoutConstraints.Loose(1000, 1000);

        private static FixedSizeBox Box(string key, float width, float height) =>
            new FixedSizeBox { Key = Key.Of(key), Size = new Vector2(width, height) };

        // Passing the child straight through keeps the assertions about the switcher rather than about
        // whatever the default transition (an Opacity) contributes to layout.
        private static Widget PassThrough(IAnimation<float> animation, Widget child) => child;

        [Test]
        public void SingleChild_SizesToThatChild()
        {
            var size = TestHarness.MountAndLayout(
                new AnimatedSwitcher
                {
                    Duration = 0f,
                    TransitionBuilder = PassThrough,
                    Child = Box("only", 30, 40),
                },
                Loose
            );

            Assert.AreEqual(new Vector2(30, 40), size);
        }

        [Test]
        public void ReplacedChild_StaysLaidOut_WhileItAnimatesOut()
        {
            // A non-zero duration leaves the outgoing controller mid-reverse, so the entry is still
            // in the tree. The ZStack then sizes to the larger of the two, which is the outgoing one.
            var state = TestHarness.Mount(Switcher(0.2f, Box("first", 80, 90)));
            TestHarness.Layout(state, Loose);

            state = TestHarness.Update(state, Switcher(0.2f, Box("second", 30, 40)));

            Assert.AreEqual(new Vector2(80, 90), TestHarness.Layout(state, Loose));
        }

        [Test]
        public void ParallelMode_StartsTheIncomingTransitionImmediately()
        {
            var first = Box("first", 80, 90);
            var second = Box("second", 30, 40);

            var animations = Capture(AnimatedSwitcherTransitionMode.Parallel, first, second);

            Assert.AreEqual(AnimationStatus.Forward, animations[second].Status);
        }

        [Test]
        public void SequentialMode_HoldsTheIncomingTransitionUntilTheOutgoingOneFinishes()
        {
            var first = Box("first", 80, 90);
            var second = Box("second", 30, 40);

            // Queued rather than started, so it reads as dismissed while the outgoing child reverses.
            var animations = Capture(AnimatedSwitcherTransitionMode.Sequential, first, second);

            Assert.AreEqual(AnimationStatus.Dismissed, animations[second].Status);
            Assert.AreEqual(AnimationStatus.Reverse, animations[first].Status);
        }

        private static Dictionary<Widget, IAnimation<float>> Capture(
            AnimatedSwitcherTransitionMode mode,
            Widget first,
            Widget second
        )
        {
            var animations = new Dictionary<Widget, IAnimation<float>>();

            Widget Record(IAnimation<float> animation, Widget child)
            {
                animations[child] = animation;
                return child;
            }

            var state = TestHarness.Mount(Switcher(0.2f, first, mode, Record));
            TestHarness.Layout(state, Loose);

            state = TestHarness.Update(state, Switcher(0.2f, second, mode, Record));
            TestHarness.Layout(state, Loose);

            return animations;
        }

        private static AnimatedSwitcher Switcher(
            float duration,
            Widget child,
            AnimatedSwitcherTransitionMode mode = AnimatedSwitcherTransitionMode.Parallel,
            AnimatedSwitcherTransitionBuilder transition = null
        ) =>
            new AnimatedSwitcher
            {
                Duration = duration,
                TransitionMode = mode,
                TransitionBuilder = transition ?? PassThrough,
                Child = child,
            };
    }
}
