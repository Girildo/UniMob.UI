using System;
using System.Collections.Generic;
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

        /// <summary>
        ///     Registers <paramref name="ticker"/> to run every frame, given the seconds since the last.
        /// </summary>
        public void AddTicker(Action<float> ticker)
        {
            _tickers.Add(ticker);
        }

        public void RemoveTicker(Action<float> ticker)
        {
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
