using NUnit.Framework;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Covers <see cref="Zone.RunningAnimations"/>, the count a frame-rate policy reads to decide
    ///     whether anything is moving.
    /// </summary>
    public class ZoneAnimationCountTests
    {
        private LifetimeController _lifetime = null!;

        [SetUp]
        public void SetUp() => _lifetime = new LifetimeController();

        [TearDown]
        public void TearDown() => _lifetime.Dispose();

        /// <summary>
        ///     The regression gate. A hosted tree always has UniMobDeviceWidget's screen poll
        ///     registered, so a count that included plain tickers would never reach zero and an app
        ///     throttling on it would never throttle again.
        /// </summary>
        [Test]
        public void APlainTicker_IsNotAnAnimation()
        {
            using var zone = TestZone.Install();

            void Poll(float deltaTime) { }

            zone.AddTicker(Poll);
            zone.Pump();

            Assert.That(zone.IsSettled, Is.False, "the ticker is registered");
            Assert.That(
                Zone.RunningAnimations,
                Is.Zero,
                "a ticker that merely watches is not an animation"
            );

            zone.RemoveTicker(Poll);
            Assert.That(Zone.RunningAnimations, Is.Zero);
        }

        [Test]
        public void AnAnimation_CountsWhileItRunsAndStopsWhenItEnds()
        {
            using var zone = TestZone.Install();
            var controller = new AnimationController(_lifetime.Lifetime, 1f);

            Assert.That(Zone.RunningAnimations, Is.Zero, "a controller at rest drives no frames");

            controller.Forward();
            Assert.That(Zone.RunningAnimations, Is.EqualTo(1));

            zone.PumpFor(2f, 0.1f);

            Assert.That(controller.IsCompleted, Is.True);
            Assert.That(
                Zone.RunningAnimations,
                Is.Zero,
                "reaching the end unregisters the ticker, one frame after the value lands"
            );
        }

        [Test]
        public void Complete_StopsTheCountImmediately()
        {
            using var zone = TestZone.Install();
            var controller = new AnimationController(_lifetime.Lifetime, 1f);

            controller.Forward();
            zone.Pump();
            Assert.That(Zone.RunningAnimations, Is.EqualTo(1));

            controller.Complete();
            Assert.That(Zone.RunningAnimations, Is.Zero);
        }

        [Test]
        public void Dismiss_StopsTheCountImmediately()
        {
            using var zone = TestZone.Install();
            var controller = new AnimationController(_lifetime.Lifetime, 1f);

            controller.Forward();
            zone.Pump(0.1f);
            Assert.That(Zone.RunningAnimations, Is.EqualTo(1));

            controller.Dismiss();
            Assert.That(Zone.RunningAnimations, Is.Zero);
        }

        /// <summary>
        ///     A zero-duration animation snaps to its end without ever registering, so it must not
        ///     hold a frame-rate policy awake for a frame it does not need.
        /// </summary>
        [Test]
        public void AZeroDurationAnimation_NeverCounts()
        {
            using var zone = TestZone.Install();
            var controller = new AnimationController(_lifetime.Lifetime, 0f);

            controller.Forward();

            Assert.That(controller.IsCompleted, Is.True);
            Assert.That(Zone.RunningAnimations, Is.Zero);
        }

        /// <summary>
        ///     Restarting is the animation layer's normal move, and a looping effect does it on every
        ///     cycle, so a double registration would inflate the count for the life of the app.
        /// </summary>
        [Test]
        public void RestartingAnAnimation_CountsItOnce()
        {
            using var zone = TestZone.Install();
            var controller = new AnimationController(_lifetime.Lifetime, 1f);

            controller.Forward();
            controller.Forward();
            controller.Reverse();

            Assert.That(Zone.RunningAnimations, Is.EqualTo(1));
        }

        [Test]
        public void ADisposedLifetime_StopsTheCountOnTheNextFrame()
        {
            using var zone = TestZone.Install();
            var owner = new LifetimeController();
            var controller = new AnimationController(owner.Lifetime, 1f);

            controller.Forward();
            Assert.That(Zone.RunningAnimations, Is.EqualTo(1));

            owner.Dispose();
            zone.Pump();

            Assert.That(Zone.RunningAnimations, Is.Zero);
        }

        [Test]
        public void ATabTransition_CountsWhileItRuns()
        {
            using var zone = TestZone.Install();
            var controller = new TabController(_lifetime.Lifetime, tabCount: 3, duration: 1f);

            Assert.That(Zone.RunningAnimations, Is.Zero);

            controller.AnimateTo(2);
            Assert.That(Zone.RunningAnimations, Is.EqualTo(1));

            zone.PumpFor(2f, 0.1f);

            Assert.That(controller.IndexIsChanging, Is.False);
            Assert.That(Zone.RunningAnimations, Is.Zero);
        }

        [Test]
        public void SettingATabValueDirectly_StopsTheCount()
        {
            using var zone = TestZone.Install();
            var controller = new TabController(_lifetime.Lifetime, tabCount: 3, duration: 1f);

            controller.AnimateTo(2);
            Assert.That(Zone.RunningAnimations, Is.EqualTo(1));

            controller.SetValue(1f);
            Assert.That(Zone.RunningAnimations, Is.Zero);
        }
    }
}
