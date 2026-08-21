using System.Collections.Generic;
using System.Threading;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     A synchronization context whose posted callbacks run when a frame is pumped, rather than
    ///     whenever Unity next gets control.
    /// </summary>
    /// <remarks>
    ///     The navigator's command loop is genuinely asynchronous, and several of its completers are
    ///     built with <c>RunContinuationsAsynchronously</c>, so completing one posts its continuation to
    ///     the ambient context instead of running it inline. Under the player loop that context is
    ///     Unity's own, pumped once a frame. A fixture driving frames by hand never returns control to
    ///     Unity, so without this the continuations would never run at all -- and a test that then read
    ///     a task's result would block the main thread forever waiting for a frame it was preventing.
    /// </remarks>
    internal sealed class PumpedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public bool IsEmpty => _queue.Count == 0;

        public override void Post(SendOrPostCallback callback, object? state) =>
            _queue.Enqueue((callback, state));

        // Send is synchronous by contract, and everything here is one thread, so running it inline is
        // both correct and what Unity's own context does on the main thread.
        public override void Send(SendOrPostCallback callback, object? state) => callback(state);

        /// <summary>
        ///     Runs everything queued, including anything queued while draining.
        /// </summary>
        /// <remarks>
        ///     Draining to empty rather than taking one pass over a snapshot: a continuation that
        ///     completes a task typically posts the next continuation immediately, and a chain of those
        ///     is one frame's work under the player loop, not one frame each.
        /// </remarks>
        public void Drain()
        {
            while (_queue.Count > 0)
            {
                var (callback, state) = _queue.Dequeue();
                callback(state);
            }
        }
    }
}
