namespace UniMob.UI.Widgets
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    ///     A route that leaves the stack with a value of a known type.
    /// </summary>
    /// <remarks>
    ///     The route is the one thing that knows what it returns, so this is where typing lives. The two
    ///     ways a value can enter the pop are both typed: the route's own <see cref="Pop(T)"/>, and the
    ///     value a <see cref="PopDecision{T}"/> carries when the route is asked. Awaiting <see cref="Result"/>
    ///     therefore yields a <see cref="PopResult{T}"/> and never an object to cast. The untyped
    ///     <see cref="Route.PopTask"/> completes as well, for callers that hold only a <see cref="Route"/>.
    /// </remarks>
    public abstract class Route<T> : Route
    {
        private readonly TaskCompletionSource<PopResult<T>> _resultCompleter =
            new TaskCompletionSource<PopResult<T>>();

        protected Route(RouteSettings settings) : base(settings)
        {
        }

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
            if (Navigator == null)
            {
                throw new InvalidOperationException(
                    "Route '" + Key + "' has never been pushed onto a navigator, so there is nothing to pop it from.");
            }

            return Navigator.PopRoute(this, PopResult.OfValue(value));
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

        internal sealed override async Task<PopVerdict> DecideAsync(object request)
        {
            var decision = await DecidePop(request);

            if (!decision.IsAllowed)
            {
                return PopVerdict.Refused;
            }

            return PopVerdict.Allowed(decision.HasValue, decision.HasValue ? (object) decision.Value : null);
        }

        protected sealed override void OnPopCompleted(PopResult result)
        {
            if (!result.HasValue)
            {
                _resultCompleter.TrySetResult(PopResult.None<T>(result.Request));
                return;
            }

            // A value reaches a typed route only through Pop(T) or PopDecision<T>, so this holds by
            // construction. The one way to break it is the untyped back-button pop with an object result,
            // and that is a programming error worth surfacing to whoever is awaiting rather than hiding.
            if (result.Value is T || result.Value == null && default(T) == null)
            {
                _resultCompleter.TrySetResult(PopResult.Of((T) result.Value, result.Request));
                return;
            }

            _resultCompleter.TrySetException(new InvalidCastException(
                "Route '" + Key + "' was popped with a " + result.Value.GetType().Name +
                " but is a Route<" + typeof(T).Name + ">."));
        }
    }
}
