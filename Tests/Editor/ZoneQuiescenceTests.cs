using NUnit.Framework;
using UniMob.Core;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Covers <see cref="Zone.IsQuiescent"/>, the predicate a harness driving the real clock waits
    ///     on before it looks at the tree.
    /// </summary>
    public class ZoneQuiescenceTests
    {
        private LifetimeController _lifetime = null!;

        [SetUp]
        public void SetUp() => _lifetime = new LifetimeController();

        [TearDown]
        public void TearDown() => _lifetime.Dispose();

        [Test]
        public void AFreshClock_HasNothingLeftToDo()
        {
            using var zone = TestZone.Install();

            AtomScheduler.Sync();

            Assert.That(Zone.IsQuiescent, Is.True);
        }

        [Test]
        public void AQueuedCallback_IsOutstandingUntilTheFrameThatRunsIt()
        {
            using var zone = TestZone.Install();

            zone.NextFrame(() => { });

            Assert.That(Zone.IsQuiescent, Is.False, "something is queued for the next frame");

            zone.Pump();

            Assert.That(Zone.IsQuiescent, Is.True, "the drain is what empties the queue");
        }

        [Test]
        public void AQueuedReaction_IsOutstandingUntilItIsSynced()
        {
            using var zone = TestZone.Install();

            var source = Atom.Value(0);
            Atom.Reaction(
                _lifetime.Lifetime,
                () =>
                {
                    var unused = source.Value;
                }
            );

            AtomScheduler.Sync();
            Assert.That(Zone.IsQuiescent, Is.True);

            source.Value = 1;
            Assert.That(
                Zone.IsQuiescent,
                Is.False,
                "dirtying a reaction queues it for actualization"
            );

            AtomScheduler.Sync();
            Assert.That(Zone.IsQuiescent, Is.True);
        }

        /// <summary>
        ///     The regression gate, and the reason this is not the fake clock's IsSettled. A hosted tree
        ///     always has UniMobDeviceWidget's screen poll registered, so a predicate that counted
        ///     tickers would never come true and a harness waiting on it would wait forever.
        /// </summary>
        [Test]
        public void APlainTicker_IsNotOutstandingWork()
        {
            using var zone = TestZone.Install();

            void Poll(float deltaTime) { }

            zone.AddTicker(Poll);
            zone.Pump();

            Assert.That(zone.IsSettled, Is.False, "the fake clock counts a registered ticker");
            Assert.That(Zone.IsQuiescent, Is.True, "this predicate deliberately does not");

            zone.RemoveTicker(Poll);
        }

        [Test]
        public void ARunningAnimation_IsNotOutstandingWork()
        {
            using var zone = TestZone.Install();
            var controller = new AnimationController(_lifetime.Lifetime, 1f);

            controller.Forward();
            zone.Pump();
            AtomScheduler.Sync();

            Assert.That(Zone.RunningAnimations, Is.EqualTo(1), "the animation is still running");
            Assert.That(
                Zone.IsQuiescent,
                Is.True,
                "whether anything is moving is RunningAnimations' question, not this one"
            );
        }

        /// <summary>
        ///     Read from outside a mounted tree, where no ZoneDriver has run, exactly as
        ///     <see cref="Zone.RunningAnimations"/> is. Nothing can be outstanding on a frame nobody
        ///     drives, so an absent clock is an answer rather than a NullReferenceException.
        /// </summary>
        [Test]
        public void WithNoClockInstalled_ItAnswers_RatherThanThrowing()
        {
            AtomScheduler.Sync();

            Assert.That(Zone.IsQuiescent, Is.True);
        }
    }
}
