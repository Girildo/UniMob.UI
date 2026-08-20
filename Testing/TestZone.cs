using System;
using System.Collections.Generic;
using UniMob.Core;
using UniMob.UI.Diagnostics;
using UniMob.UI.Rendering;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     A frame clock a test drives by hand, so a test that needs frames does not need a player loop.
    /// </summary>
    /// <remarks>
    ///     An ambient static clock is a testability smell, blessed deliberately: threading the zone
    ///     through <c>BuildContext</c> goes viral through every widget, and Flutter's
    ///     <c>SchedulerBinding.instance</c> is the same ambient singleton swapped the same way.
    ///     <para>
    ///         <see cref="Install"/> restores the previous clock on dispose, so a fixture cannot leave
    ///         its fake behind. Faults are captured for the same span, through
    ///         <see cref="UniMobError.Override"/> rather than through a method on the clock -- reporting
    ///         an exception has nothing to do with keeping time.
    ///     </para>
    /// </remarks>
    public sealed class TestZone : Zone, IDisposable
    {
        public const float DefaultDeltaTime = 1f / 60f;

        private readonly IDisposable _zoneScope;
        private readonly RecordingErrors _errors;

        private TestZone()
        {
            _errors = RecordingErrors.Capture();
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
        ///     Whether there is nothing left to do: no ticker running, nothing queued for the next
        ///     frame, and no atom queued for actualization.
        /// </summary>
        /// <remarks>
        ///     A direct question, where counting quiet frames only ever inferred the answer. The
        ///     inference could be wrong in both directions -- it passed with work still queued whenever
        ///     a deferral chain paused for longer than the count, and it paid for its own frames every
        ///     time it was right.
        /// </remarks>
        public bool IsSettled =>
            !HasActiveTickers && NextFrameQueueEmpty && !AtomScheduler.HasPendingWork;

        /// <summary>
        ///     Runs one whole frame, in the order the real player loop runs its drivers.
        /// </summary>
        /// <remarks>
        ///     The order is measured, not documented: the zone ticks and drains, the scheduler then
        ///     actualizes what those invalidated, and the geometry ticker runs last because its driver
        ///     is a <c>LateUpdate</c>. <c>ZoneDriverTests</c> holds that against the real loop.
        /// </remarks>
        public void Pump(float deltaTime = DefaultDeltaTime)
        {
            RunTickers(deltaTime);
            DrainNextFrame();
            AtomScheduler.Sync();
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
        /// <param name="onFrame">
        ///     Run after each frame. For a caller that has to do a frame's other work itself: the
        ///     navigator must reconcile its child state tree every frame or a popped route is never
        ///     disposed and the trace quietly omits it.
        /// </param>
        /// <param name="pending">
        ///     Outstanding work this clock cannot see, which also has to finish. The navigator counts
        ///     handlers entered but not left; without it, silence ends a pop while its exit animation is
        ///     still running.
        /// </param>
        /// <param name="diagnostic">What to print if the deadline is reached.</param>
        /// <remarks>
        ///     <paramref name="maxFrames"/> is a deadline so a broken system fails loudly instead of
        ///     hanging. Reaching it is a finding, not a flake: something entered a handler it never
        ///     left, or kept working indefinitely.
        /// </remarks>
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
            _zoneScope.Dispose();
            _errors.Dispose();
        }
    }
}
