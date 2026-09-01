using System;
using System.Collections.Generic;
using UniMob.Core;
using UniMob.UI.Diagnostics;

namespace UniMob
{
    /// <summary>
    ///     The frame clock: a set of tickers, and a queue of callbacks to run on the next frame. Not
    ///     Dart's error-interception zone, despite the name -- see CONTEXT.md.
    /// </summary>
    /// <remarks>
    ///     The hierarchy is closed. The constructor is internal, so the type can be derived from inside
    ///     this package and its friends and nowhere else; it is public only because a public fake clock
    ///     cannot derive from an internal base.
    /// </remarks>
    public abstract class Zone
    {
        private readonly List<Action<float>> _tickers = new List<Action<float>>();
        private readonly HashSet<Action<float>> _animationTickers = new HashSet<Action<float>>();

        private List<Action> _nextFrame = new List<Action>();
        private List<Action> _nextFrameExecuting = new List<Action>();

        internal Zone() { }

        // Assigned by ZoneDriver.Init, which the runtime calls before any scene loads.
        internal static Zone Current { get; private set; } = null!;

        internal static void Install(Zone zone) => Current = zone;

        /// <summary>
        ///     Installs <paramref name="zone"/> until the returned scope is disposed. There is no
        ///     setter, so a fake clock cannot outlive the scope that installed it.
        /// </summary>
        internal static IDisposable Override(Zone zone)
        {
            if (zone == null)
            {
                throw new ArgumentNullException(nameof(zone));
            }

            var scope = new Scope(Current);
            Current = zone;
            return scope;
        }

        /// <summary>Whether any ticker is registered. Part of having nothing left to do.</summary>
        protected bool HasActiveTickers => _tickers.Count > 0;

        /// <summary>Whether anything is queued for the next frame.</summary>
        /// <remarks>
        ///     Wider than <see cref="HasActiveTickers"/> because a harness driving the real clock has
        ///     to ask this of <see cref="Current"/>, which it cannot derive from.
        /// </remarks>
        protected internal bool NextFrameQueueEmpty => _nextFrame.Count == 0;

        /// <summary>How many animations the clock is currently driving.</summary>
        /// <remarks>
        ///     A frame-rate policy cannot ask whether the clock is busy: a hosted tree always has the
        ///     device widget's screen poll registered, so <see cref="HasActiveTickers"/> is permanently
        ///     true. This counts only the tickers that exist because something is moving.
        /// </remarks>
        // Null-checked, unlike every other Current call site, because this one is read from outside a
        // mounted tree, where no ZoneDriver has run.
        public static int RunningAnimations => Current?._animationTickers.Count ?? 0;

        /// <summary>
        ///     Whether the clock and the reactive graph have nothing left to do: no atom queued for
        ///     actualization, and nothing queued for the following frame. True where no clock has been
        ///     installed, since nothing can be outstanding on a frame nobody is driving.
        /// </summary>
        /// <remarks>
        ///     Tickers are deliberately no part of this. A hosted tree always has the device widget's
        ///     screen poll registered, so a registered ticker means something is watching rather than
        ///     something is outstanding, and what a tick produces lands in the scheduler anyway. A
        ///     predicate that included them would never come true for a tree hosted the way an app hosts
        ///     one, and a harness waiting on it would wait forever. Whether anything is moving is the
        ///     separate question <see cref="RunningAnimations"/> answers.
        ///     <para>
        ///         Public so that a harness driving the real clock can ask it: <see cref="Current"/> is
        ///         not, and neither is the queue.
        ///     </para>
        /// </remarks>
        // Null-checked for the same reason RunningAnimations is.
        public static bool IsQuiescent =>
            !AtomScheduler.HasPendingWork && (Current?.NextFrameQueueEmpty ?? true);

        /// <summary>
        ///     Registers <paramref name="ticker"/> to run every frame, given the seconds since the last.
        /// </summary>
        public void AddTicker(Action<float> ticker)
        {
            _tickers.Add(ticker);
        }

        /// <summary>
        ///     Registers <paramref name="ticker"/> as an animation: a ticker that exists only while
        ///     something is moving, and so counts towards <see cref="RunningAnimations"/>. Registering
        ///     one twice does nothing, which is what lets an animation restart without leaking a count.
        /// </summary>
        internal void AddAnimationTicker(Action<float> ticker)
        {
            if (!_animationTickers.Add(ticker))
            {
                return;
            }

            _tickers.Add(ticker);
        }

        public void RemoveTicker(Action<float> ticker)
        {
            // Dropped from both, so an animation leaving by the general path cannot strand the count.
            _animationTickers.Remove(ticker);
            _tickers.Remove(ticker);
        }

        /// <summary>Runs <paramref name="callback"/> once, on the frame after this one.</summary>
        public void NextFrame(Action callback)
        {
            _nextFrame.Add(callback);
        }

        /// <summary>Phase one of a frame: every ticker runs, given <paramref name="deltaTime"/>.</summary>
        protected void RunTickers(float deltaTime)
        {
            // Backwards, because a ticker is entitled to remove itself while running.
            for (var i = _tickers.Count - 1; i >= 0; i--)
            {
                try
                {
                    _tickers[i].Invoke(deltaTime);
                }
                catch (Exception ex)
                {
                    UniMobError.Report(new UniMobFault(ex, "Ticker"));
                }
            }
        }

        /// <summary>Phase two of a frame: everything queued by <see cref="NextFrame"/> runs.</summary>
        protected void DrainNextFrame()
        {
            // Swapped before draining, so a callback that queues another does not extend this frame.
            var toSwap = _nextFrame;
            _nextFrame = _nextFrameExecuting;
            _nextFrameExecuting = toSwap;

            foreach (var callback in _nextFrameExecuting)
            {
                try
                {
                    callback.Invoke();
                }
                catch (Exception ex)
                {
                    UniMobError.Report(new UniMobFault(ex, "NextFrame"));
                }
            }

            _nextFrameExecuting.Clear();
        }

        private sealed class Scope : IDisposable
        {
            private readonly Zone? _previous;
            private bool _disposed;

            public Scope(Zone? previous) => _previous = previous;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Current = _previous!;
            }
        }
    }
}
