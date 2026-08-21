using System;
using System.Collections.Generic;
using System.Threading;
using UniMob.Core;
using UniMob.UI.Diagnostics;
using UniMob.UI.Rendering;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     A frame clock a test drives by hand, so a test that needs frames does not need a player loop.
    ///     Installed and captured for the length of a <c>using</c> block. See CONTEXT.md.
    /// </summary>
    public sealed class TestZone : Zone, IDisposable
    {
        public const float DefaultDeltaTime = 1f / 60f;

        private readonly IDisposable _zoneScope;
        private readonly RecordingErrors _errors;
        private readonly PumpedSynchronizationContext _continuations = new();
        private readonly SynchronizationContext? _previousContext;

        private TestZone()
        {
            _errors = RecordingErrors.Capture();
            _previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(_continuations);
            _zoneScope = Override(this);
        }

        /// <summary>Installs a fresh clock until the returned instance is disposed.</summary>
        public static TestZone Install() => new TestZone();

        /// <summary>Seconds advanced, as the sum of the deltas pumped.</summary>
        public float Elapsed { get; private set; }

        /// <summary>Frames pumped.</summary>
        public int FrameCount { get; private set; }

        /// <summary>Every fault reported while this clock has been installed.</summary>
        public IReadOnlyList<UniMobFault> Faults => _errors;

        /// <summary>
        ///     Whether nothing is left to do: no ticker registered, nothing queued for the next frame,
        ///     no continuation waiting, and no atom queued for actualization.
        /// </summary>
        public bool IsSettled =>
            !HasActiveTickers
            && NextFrameQueueEmpty
            && _continuations.IsEmpty
            && !AtomScheduler.HasPendingWork;

        /// <summary>
        ///     Runs one whole frame, in the order the player loop runs its drivers. See CONTEXT.md.
        /// </summary>
        public void Pump(float deltaTime = DefaultDeltaTime)
        {
            // This order is measured, and ZoneDriverTests holds it against the real player loop.
            // Reordering it moves the navigator's golden traces.
            RunTickers(deltaTime);
            DrainNextFrame();
            AtomScheduler.Sync();
            _continuations.Drain();
            WidgetGeometryTicker.Tick();

            Elapsed += deltaTime;
            FrameCount++;
        }

        public void PumpFrames(int count, float deltaTime = DefaultDeltaTime)
        {
            for (var frame = 0; frame < count; frame++)
            {
                Pump(deltaTime);
            }
        }

        /// <summary>Pumps whole frames until at least <paramref name="seconds"/> have elapsed.</summary>
        public void PumpFor(float seconds, float deltaTime = DefaultDeltaTime)
        {
            var target = Elapsed + seconds;

            while (Elapsed < target)
            {
                Pump(deltaTime);
            }
        }

        /// <summary>
        ///     Pumps until nothing is left to do, or fails at <paramref name="maxFrames"/>.
        /// </summary>
        /// <param name="onFrame">Run after each frame, for work a caller owns itself.</param>
        /// <param name="pending">Outstanding work this clock cannot see, which also has to finish.</param>
        /// <param name="diagnostic">What to report if the deadline is reached.</param>
        /// <exception cref="TimeoutException">
        ///     Nothing settled within <paramref name="maxFrames"/>. A finding, not a flake: something
        ///     entered a handler it never left.
        /// </exception>
        public void Settle(
            int maxFrames = 300,
            Action? onFrame = null,
            Func<bool>? pending = null,
            Func<string>? diagnostic = null,
            float deltaTime = DefaultDeltaTime
        )
        {
            for (var frame = 0; frame < maxFrames; frame++)
            {
                Pump(deltaTime);
                onFrame?.Invoke();

                if (IsSettled && pending?.Invoke() != true)
                {
                    return;
                }
            }

            throw new TimeoutException(
                $"Did not settle within {maxFrames} frames."
                    + (diagnostic == null ? string.Empty : "\n\n" + diagnostic.Invoke())
            );
        }

        public void Dispose()
        {
            SynchronizationContext.SetSynchronizationContext(_previousContext);
            _zoneScope.Dispose();
            _errors.Dispose();
        }
    }
}
