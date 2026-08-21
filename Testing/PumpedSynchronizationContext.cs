using System.Collections.Generic;
using System.Threading;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     A synchronization context whose posted callbacks run when a frame is pumped, rather than
    ///     whenever Unity next gets control.
    /// </summary>
    /// <remarks>
    ///     Stands in for Unity's own context, which the player loop pumps once a frame. A fixture
    ///     driving frames by hand never returns control to Unity, so without this a posted continuation
    ///     would never run. See CONTEXT.md.
    /// </remarks>
    internal sealed class PumpedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public bool IsEmpty => _queue.Count == 0;

        public override void Post(SendOrPostCallback callback, object? state) =>
            _queue.Enqueue((callback, state));

        public override void Send(SendOrPostCallback callback, object? state) => callback(state);

        /// <summary>Runs everything queued, including anything queued while draining.</summary>
        public void Drain()
        {
            // To empty, not one pass: a continuation usually posts the next one, and that chain is
            // one frame's work under the player loop rather than one frame each.
            while (_queue.Count > 0)
            {
                var (callback, state) = _queue.Dequeue();
                callback(state);
            }
        }
    }
}
