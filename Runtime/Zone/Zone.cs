using System;
using System.Collections.Generic;
using UniMob.UI.Diagnostics;

namespace UniMob
{
    /// <summary>
    ///     The frame clock: a set of tickers, and a queue of callbacks to run on the next frame.
    /// </summary>
    /// <remarks>
    ///     Not Dart's error-interception zone, despite the name. Reporting a caught exception belongs to
    ///     <see cref="UniMobError"/>, which needs no clock.
    ///     <para>
    ///         The hierarchy is closed: the constructor is internal, so the type can be named and derived
    ///         from inside this package and its friends, and nowhere else. It is public only because a
    ///         public fake clock cannot derive from an internal base.
    ///     </para>
    ///     <para>
    ///         The two phases are separate methods rather than one <c>Update</c> so that a fake can run
    ///         the other frame drivers between them, in the order the real player loop runs them.
    ///     </para>
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
        ///     Installs <paramref name="zone"/> until the returned scope is disposed.
        /// </summary>
        /// <remarks>
        ///     Follows <see cref="UniMobDiagnostics.Override"/>'s scope discipline: there is no setter,
        ///     so "a test left its fake clock installed" is unrepresentable rather than merely
        ///     discouraged. A leak is bounded anyway -- ZoneDriver reinstalls the real clock whenever
        ///     play mode starts, and a domain reload clears the slot outright.
        /// </remarks>
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
        protected bool NextFrameQueueEmpty => _nextFrame.Count == 0;

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
        /// <remarks>
        ///     The queue is swapped before it is drained, so a callback that queues another does not
        ///     extend the frame it is running on.
        /// </remarks>
        protected void DrainNextFrame()
        {
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
