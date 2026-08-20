using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Pins the order Unity runs the three frame drivers in, against the real player loop.
    /// </summary>
    /// <remarks>
    ///     A fake clock is sequenced to match this, so something has to establish what "this" is. The
    ///     order was incidental before <see cref="DefaultExecutionOrderAttribute"/> was applied to the
    ///     zone: both Update-phase components sat at the default order, and the scheduler's GameObject
    ///     is created lazily on first actualize, so the first frames of a run could disagree with the
    ///     rest of it. Golden traces encode the resulting latency, which is why it is pinned and then
    ///     asserted rather than assumed.
    /// </remarks>
    public class ZoneDriverTests
    {
        private sealed class LateDirtier : MonoBehaviour
        {
            public MutableAtom<int>? Source;
            public List<(string Label, int Frame)>? Log;

            // LateUpdate, so every following frame begins with work already queued for the scheduler
            // rather than acquiring it partway through. Without that, what a frame records is when the
            // invalidation was enqueued rather than which driver ran first.
            private void LateUpdate()
            {
                Log?.Add(("late", Time.frameCount));

                if (Source != null)
                {
                    Source.Value += 1;
                }
            }
        }

        [UnityTest]
        public IEnumerator FrameDrivers_RunInThePinnedOrder()
        {
            var lifetime = new LifetimeController();
            var log = new List<(string Label, int Frame)>();
            var source = Atom.Value(0);

            Atom.Reaction(
                lifetime.Lifetime,
                () =>
                {
                    var unused = source.Value;
                    log.Add(("sync", Time.frameCount));
                }
            );

            void Ticker(float deltaTime) => log.Add(("tick", Time.frameCount));

            var dirtier = new GameObject(nameof(LateDirtier)).AddComponent<LateDirtier>();
            Zone.Current.AddTicker(Ticker);
            try
            {
                // Let the reaction's first run and the scheduler's lazily created GameObject settle,
                // so what follows is steady state rather than warm-up.
                yield return null;
                yield return null;
                log.Clear();
                dirtier.Log = log;
                dirtier.Source = source;

                for (var i = 0; i < 6; i++)
                {
                    yield return null;
                }
            }
            finally
            {
                Zone.Current.RemoveTicker(Ticker);
                Object.Destroy(dirtier.gameObject);
                lifetime.Dispose();
            }

            // The frame the coroutine resumes on is cut short, so judge only whole frames.
            var wholeFrames = log.GroupBy(entry => entry.Frame)
                .Where(frame => frame.Count() == 3)
                .OrderBy(frame => frame.Key)
                .ToList();

            Assert.That(
                wholeFrames,
                Has.Count.GreaterThanOrEqualTo(4),
                "Too few whole frames to judge an order: " + Describe(log)
            );

            foreach (var frame in wholeFrames)
            {
                Assert.That(
                    frame.Select(entry => entry.Label),
                    Is.EqualTo(new[] { "tick", "sync", "late" }),
                    $"Frame {frame.Key} ran its drivers out of order: " + Describe(log)
                );
            }
        }

        private static string Describe(IEnumerable<(string Label, int Frame)> log) =>
            string.Join(" | ", log.Select(entry => $"{entry.Label}@{entry.Frame}"));
    }
}
