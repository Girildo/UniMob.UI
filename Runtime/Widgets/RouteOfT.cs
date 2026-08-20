using System;
using System.Threading.Tasks;

namespace UniMob.UI.Widgets
{
    /// <summary>
    ///     A route that leaves the stack with a value of a known type.
    /// </summary>
    /// <remarks>
    ///     The two ways a value can enter the pop are both typed: the route's own <see cref="Pop(T)"/>, and the
    ///     value a <see cref="PopDecision{T}"/> carries when the route is asked. Awaiting <see cref="Result"/>
    ///     therefore yields a <see cref="PopResult{T}"/>. The untyped
    ///     <see cref="Route.PopTask"/> completes as well, for callers that hold only a <see cref="Route"/>.
    /// </remarks>
    public abstract class Route<T> : Route
    {
        // Asynchronous continuations, like the untyped completer: see Route.CompletePop.
        private readonly TaskCompletionSource<PopResult<T>> _resultCompleter =
            new TaskCompletionSource<PopResult<T>>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

        protected Route(RouteSettings settings)
            : base(settings) { }

        /// <summary>
        ///     Completes when the route has left the stack, with what it left with, typed.
        /// </summary>
        public Task<PopResult<T>> Result => _resultCompleter.Task;

        /// <summary>
        ///     Closes the route with a value, on its own authority. See <see cref="Route.Pop"/> for the
        ///     terms; this is the same operation carrying a result.
        /// </summary>
        public Task<PopOutcome> Pop(T value)
        {
            return Navigator == null
                ? Task.FromResult(PopOutcome.NotTopmost)
                : Navigator.PopRoute(this, PopResult.OfValue(value));
        }

        /// <summary>
        ///     Consulted when chrome or the system asks for this route to be popped. The typed
        ///     counterpart of <see cref="Route.OnPopRequested"/>: answer <see cref="PopDecision{T}.Refuse"/>
        ///     to stay, <see cref="PopDecision{T}.Allow()"/> to leave without a value, or
        ///     <see cref="PopDecision{T}.Allow(T)"/> to leave with one. Same freedoms as the untyped hook:
        ///     it runs outside the navigator's command loop and may navigate.
        /// </summary>
        protected virtual Task<PopDecision<T>> DecidePop(object request)
        {
            return Task.FromResult(PopDecision<T>.Allow());
        }

        /// <summary>
        ///     Sealed because a typed route answers through <see cref="DecidePop"/>; an override here would
        ///     never be consulted.
        /// </summary>
        protected sealed override Task<PopDecision> OnPopRequested(object request)
        {
            return base.OnPopRequested(request);
        }

        internal sealed override async Task<PopDecision> DecideAsync(object request) =>
            await DecidePop(request);

        protected sealed override void OnPopCompleted(PopResult result)
        {
            if (!result.HasValue)
            {
                _resultCompleter.TrySetResult(
                    new PopResult<T>(result.Cause, result.Request, false, default)
                );
                return;
            }

            // A value reaches a typed route only through Pop(T) or PopDecision<T>, so this holds by
            // construction; a value of another type here is a bug in the package, surfaced to whoever is
            // awaiting rather than hidden.
            if (result.Value is T || result.Value == null && default(T) == null)
            {
                _resultCompleter.TrySetResult(
                    new PopResult<T>(result.Cause, result.Request, true, (T)result.Value)
                );
                return;
            }

            _resultCompleter.TrySetException(
                new InvalidCastException(
                    "Route '"
                        + Key
                        + "' was popped with a "
                        + result.Value.GetType().Name
                        + " but is a Route<"
                        + typeof(T).Name
                        + ">."
                )
            );
        }
    }
}
