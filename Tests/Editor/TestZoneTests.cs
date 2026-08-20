using System;
using NUnit.Framework;
using UniMob.Core;
using UniMob.UI.Diagnostics;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Covers the fake clock itself, in EditMode, with no player loop anywhere.
    /// </summary>
    public class TestZoneTests
    {
        private LifetimeController _lifetime = null!;

        [SetUp]
        public void SetUp() => _lifetime = new LifetimeController();

        [TearDown]
        public void TearDown() => _lifetime.Dispose();

        /// <summary>
        ///     Everything the settle contract promises rests on the scheduler being able to answer
        ///     whether it still has queued work, so that claim is checked directly rather than assumed.
        /// </summary>
        [Test]
        public void HasPendingWork_IsTrueWhileAReactionIsQueuedAndFalseOnceSynced()
        {
            using var zone = TestZone.Install();

            var source = Atom.Value(0);
            var runs = 0;
            Atom.Reaction(
                _lifetime.Lifetime,
                () =>
                {
                    var unused = source.Value;
                    runs++;
                }
            );

            AtomScheduler.Sync();
            Assert.That(AtomScheduler.HasPendingWork, Is.False, "nothing is queued after a sync");
            Assert.That(runs, Is.EqualTo(1), "the reaction ran once on activation");

            source.Value = 1;
            Assert.That(
                AtomScheduler.HasPendingWork,
                Is.True,
                "dirtying a reaction queues it for actualization"
            );

            AtomScheduler.Sync();
            Assert.That(AtomScheduler.HasPendingWork, Is.False);
            Assert.That(runs, Is.EqualTo(2), "the sync is what re-ran it");
        }

        [Test]
        public void Pump_RunsEveryTickerWithTheDeltaItWasGiven()
        {
            using var zone = TestZone.Install();

            var deltas = new System.Collections.Generic.List<float>();
            void Ticker(float deltaTime) => deltas.Add(deltaTime);

            zone.AddTicker(Ticker);
            zone.Pump(0.25f);
            zone.Pump(0.5f);
            zone.RemoveTicker(Ticker);
            zone.Pump(0.25f);

            Assert.That(deltas, Is.EqualTo(new[] { 0.25f, 0.5f }));
            Assert.That(zone.FrameCount, Is.EqualTo(3), "a removed ticker does not stop the clock");
            Assert.That(zone.Elapsed, Is.EqualTo(1f).Within(1e-5f));
        }

        /// <summary>
        ///     Pins what "next frame" actually means, which depends on who queued it. The drain is the
        ///     second half of a frame, so a ticker's callback runs later in the frame that queued it,
        ///     while anything queued from outside a frame waits for the next one. The real zone has
        ///     always behaved this way; the name is what is imprecise, not the clock.
        /// </summary>
        [Test]
        public void NextFrame_QueuedFromATicker_RunsLaterInTheSameFrame()
        {
            using var zone = TestZone.Install();

            var ranOnFrame = -1;
            void Ticker(float deltaTime) => zone.NextFrame(() => ranOnFrame = zone.FrameCount);

            zone.AddTicker(Ticker);
            zone.Pump();

            Assert.That(
                ranOnFrame,
                Is.Zero,
                "the drain follows the tickers within one frame, so FrameCount has not advanced yet"
            );
        }

        [Test]
        public void NextFrame_QueuedFromOutsideAFrame_WaitsForTheNextPump()
        {
            using var zone = TestZone.Install();

            var ran = false;
            zone.NextFrame(() => ran = true);

            Assert.That(ran, Is.False, "queueing alone runs nothing");

            zone.Pump();

            Assert.That(ran, Is.True);
        }

        /// <summary>
        ///     The sharpness this whole change buys: an exact mid-flight value at a named time, which a
        ///     fixture that waited on real frames could only approximate.
        /// </summary>
        /// <remarks>
        ///     A delta of 1/64 rather than a friendlier 1/100 because it is exactly representable, so
        ///     the assertion is about the animation rather than about binary floating point. The
        ///     smoothing is idempotent under a constant delta -- 0.8*dt + 0.2*dt == dt -- which is what
        ///     makes half of a one-second animation exactly half.
        /// </remarks>
        [Test]
        public void PumpFrames_AdvancesAnAnimationToAnExactValue()
        {
            using var zone = TestZone.Install();

            var animation = new AnimationController(_lifetime.Lifetime, duration: 1f);
            animation.Forward();

            zone.PumpFrames(32, deltaTime: 1f / 64f);

            Assert.That(animation.Value, Is.EqualTo(0.5f).Within(1e-6f));
        }

        /// <summary>
        ///     PumpFor advances in whole frames, so it stops at the first frame boundary at or past the
        ///     time asked for. It cannot land mid-frame, and does not pretend to.
        /// </summary>
        [Test]
        public void PumpFor_AdvancesAtLeastTheTimeAskedFor_ByWholeFrames()
        {
            using var zone = TestZone.Install();

            const float DeltaTime = 1f / 100f;
            zone.PumpFor(0.5f, DeltaTime);

            Assert.That(zone.Elapsed, Is.GreaterThanOrEqualTo(0.5f));
            Assert.That(
                zone.Elapsed,
                Is.LessThan(0.5f + DeltaTime),
                "it stops at the first frame that satisfies the request, not later"
            );
        }

        [Test]
        public void Settle_ReturnsAsSoonAsThereIsNothingLeftToDo()
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

            zone.Settle();

            Assert.That(zone.IsSettled, Is.True);
            Assert.That(
                zone.FrameCount,
                Is.LessThan(5),
                "settling stops at quiescence rather than paying for a fixed number of frames"
            );
        }

        [Test]
        public void Settle_FailsAtItsDeadlineRatherThanHanging()
        {
            using var zone = TestZone.Install();

            // A ticker that never unregisters is never settled, which is the shape of a handler that
            // was entered and never left.
            void Ticker(float deltaTime) { }

            zone.AddTicker(Ticker);

            var thrown = Assert.Throws<TimeoutException>(() =>
                zone.Settle(maxFrames: 5, diagnostic: () => "the diagnostic the caller supplied")
            );

            Assert.That(thrown!.Message, Does.Contain("5 frames"));
            Assert.That(
                thrown.Message,
                Does.Contain("the diagnostic the caller supplied"),
                "a deadline is a finding, so it reports what the caller knows"
            );
        }

        [Test]
        public void Faults_AreCapturedForTheLifeOfTheClock()
        {
            using var zone = TestZone.Install();

            void Ticker(float deltaTime) => throw new InvalidOperationException("ticker threw");

            zone.AddTicker(Ticker);
            zone.Pump();

            Assert.That(zone.Faults, Has.Count.EqualTo(1));
            Assert.That(zone.Faults[0].Phase, Is.EqualTo("Ticker"));
            Assert.That(zone.Faults[0].Exception.Message, Is.EqualTo("ticker threw"));
        }

        [Test]
        public void Install_RestoresThePreviousClockOnDispose()
        {
            var outer = TestZone.Install();
            try
            {
                Assert.That(Zone.Current, Is.SameAs(outer));

                using (var inner = TestZone.Install())
                {
                    Assert.That(Zone.Current, Is.SameAs(inner));
                }

                Assert.That(
                    Zone.Current,
                    Is.SameAs(outer),
                    "a scope restores what it displaced, so a leak is unrepresentable"
                );
            }
            finally
            {
                outer.Dispose();
            }
        }
    }
}
