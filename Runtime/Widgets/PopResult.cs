namespace UniMob.UI.Widgets
{
    /// <summary>
    ///     Why a route left the stack, as the navigator knows it. Separate from <see cref="PopResult.Request"/>,
    ///     which is the requester's own object and which the navigator never interprets.
    /// </summary>
    public enum PopCause
    {
        /// <summary>The route popped itself, through <see cref="Route.Pop"/>. Nobody asked.</summary>
        Self,

        /// <summary>
        ///     Someone asked and the route agreed. <see cref="PopResult.Request"/> holds what they asked with.
        /// </summary>
        Requested,

        /// <summary>
        ///     The navigator removed the route without asking it: unmount, an un-asked <c>Replace</c> or
        ///     <c>NewRoot</c>.
        /// </summary>
        Teardown,
    }

    /// <summary>
    ///     What a route left the stack with: why it left, what it was asked with if it was asked, and the
    ///     value if it carried one.
    /// </summary>
    /// <remarks>
    ///     <see cref="HasValue"/> rather than a nullable value: for value types, default is a legitimate
    ///     result. <see cref="Request"/> is whatever the requester passed to <c>RequestPop</c>, carried
    ///     through uninterpreted, and is null unless <see cref="Cause"/> is <see cref="PopCause.Requested"/>.
    /// </remarks>
    public readonly struct PopResult
    {
        public PopCause Cause { get; }
        public object Request { get; }
        public bool HasValue { get; }
        public object Value { get; }

        internal PopResult(PopCause cause, object request, bool hasValue, object value)
        {
            Cause = cause;
            Request = request;
            HasValue = hasValue;
            Value = value;
        }

        public void Deconstruct(out bool hasValue, out object value)
        {
            hasValue = HasValue;
            value = Value;
        }

        /// <summary>
        ///     A result without a value: the route's own close, or an asked one when <paramref name="request"/>
        ///     is given.
        /// </summary>
        public static PopResult None(object request = null) => new PopResult(CauseOf(request), request, false, null);

        /// <summary>A result carrying an untyped value. Typed routes go through <see cref="Of{T}"/>.</summary>
        public static PopResult OfValue(object value, object request = null) =>
            new PopResult(CauseOf(request), request, true, value);

        public static PopResult<T> Of<T>(T value, object request = null) =>
            new PopResult<T>(CauseOf(request), request, true, value);

        public static PopResult<T> None<T>(object request = null) =>
            new PopResult<T>(CauseOf(request), request, false, default);

        /// <summary>The result of a route the navigator removed without asking it.</summary>
        public static PopResult Teardown() => new PopResult(PopCause.Teardown, null, false, null);

        public static PopResult<T> Teardown<T>() => new PopResult<T>(PopCause.Teardown, null, false, default);

        private static PopCause CauseOf(object request) => request == null ? PopCause.Self : PopCause.Requested;

        public override string ToString() => Describe(HasValue, Value, Cause, Request);

        internal static string Describe(bool hasValue, object value, PopCause cause, object request) =>
            (hasValue ? "PopResult(" + value + ")" : "PopResult(none)") + ", " + cause +
            (request == null ? "" : " by " + request);
    }

    /// <summary>
    ///     <see cref="PopResult"/> for a <see cref="Route{T}"/>: the value is a <typeparamref name="T"/>,
    ///     by construction rather than by cast.
    /// </summary>
    public readonly struct PopResult<T>
    {
        public PopCause Cause { get; }
        public object Request { get; }
        public bool HasValue { get; }
        public T Value { get; }

        internal PopResult(PopCause cause, object request, bool hasValue, T value)
        {
            Cause = cause;
            Request = request;
            HasValue = hasValue;
            Value = value;
        }

        public void Deconstruct(out bool hasValue, out T value)
        {
            hasValue = HasValue;
            value = Value;
        }

        public override string ToString() => PopResult.Describe(HasValue, Value, Cause, Request);
    }
}
