namespace UniMob.UI.Widgets
{
    /// <summary>
    ///     A route's answer to being asked whether it may be popped: yes or no. An untyped route cannot
    ///     attach a value; a <see cref="Route{T}"/> answers with <see cref="PopDecision{T}"/>, which
    ///     converts to this for the navigator.
    /// </summary>
    public readonly struct PopDecision
    {
        public bool IsAllowed { get; }

        // The value a typed decision carries, erased: this is the shape the navigator consumes, and only a
        // PopDecision<T> can set it.
        internal bool HasValue { get; }
        internal object Value { get; }

        internal PopDecision(bool isAllowed, bool hasValue, object value)
        {
            IsAllowed = isAllowed;
            HasValue = hasValue;
            Value = value;
        }

        public static PopDecision Allow() => new PopDecision(true, false, null);

        public static PopDecision Refuse() => new PopDecision(false, false, null);

        /// <summary>The result a pop carries when this decision answered <paramref name="request"/>.</summary>
        internal PopResult ToResult(object request) => new PopResult(PopCause.Requested, request, HasValue, Value);
    }

    /// <summary>
    ///     A typed route's answer to being asked whether it may be popped: refuse, allow without a value,
    ///     or allow and supply the value the pop carries.
    /// </summary>
    public readonly struct PopDecision<T>
    {
        public bool IsAllowed { get; }
        public bool HasValue { get; }
        public T Value { get; }

        private PopDecision(bool isAllowed, bool hasValue, T value)
        {
            IsAllowed = isAllowed;
            HasValue = hasValue;
            Value = value;
        }

        public static PopDecision<T> Allow(T value) => new PopDecision<T>(true, true, value);

        public static PopDecision<T> Allow() => new PopDecision<T>(true, false, default);

        public static PopDecision<T> Refuse() => new PopDecision<T>(false, false, default);

        public static implicit operator PopDecision(PopDecision<T> decision) =>
            new PopDecision(decision.IsAllowed, decision.HasValue, decision.HasValue ? (object) decision.Value : null);
    }

    /// <summary>
    ///     What became of a pop, whether the route's own or a requested one.
    /// </summary>
    public enum PopOutcome
    {
        /// <summary>The route left the stack.</summary>
        Popped,

        /// <summary>The route was asked and said no.</summary>
        Refused,

        /// <summary>
        ///     Nothing happened: the route was not topmost when the pop reached the navigator, either
        ///     because it had already left the stack or because something has since been pushed over it.
        /// </summary>
        NotTopmost,

        /// <summary>Nothing happened: the route is the navigator's last one and cannot be popped.</summary>
        LastRoute,
    }

    /// <summary>
    ///     Where a <c>RequestPopTo</c> ended: at its target, or at the first route that would not go.
    /// </summary>
    public readonly struct PopToOutcome
    {
        public bool Reached { get; }

        /// <summary>The route the walk stopped at, when it did not reach its target.</summary>
        public Route StoppedAt { get; }

        /// <summary>Why the walk stopped there, when it did not reach its target.</summary>
        public PopOutcome Outcome { get; }

        private PopToOutcome(bool reached, Route stoppedAt, PopOutcome outcome)
        {
            Reached = reached;
            StoppedAt = stoppedAt;
            Outcome = outcome;
        }

        /// <summary>A walk that reached its target.</summary>
        public static PopToOutcome ReachedTarget() => new PopToOutcome(true, null, PopOutcome.Popped);

        /// <summary>A walk that stopped short, at <paramref name="at"/>, because of <paramref name="outcome"/>.</summary>
        public static PopToOutcome Stopped(Route at, PopOutcome outcome) => new PopToOutcome(false, at, outcome);
    }
}
