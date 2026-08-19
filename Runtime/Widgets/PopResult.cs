namespace UniMob.UI.Widgets
{
    /// <summary>
    ///     The reasons the package itself puts in <see cref="PopResult.Request"/>: <see cref="Teardown"/>
    ///     when the navigator removes a route without asking it (unmount, an un-asked <c>Replace</c> or
    ///     <c>NewRoot</c>), <see cref="Back"/> when the back button asks. Everything else a route is asked
    ///     with is the caller's own request object, carried through uninterpreted; a null request means
    ///     the route closed itself.
    /// </summary>
    public static class RemovalReason
    {
        public static readonly object Teardown = new Marker("Teardown");
        public static readonly object Back = new Marker("Back");

        private sealed class Marker
        {
            private readonly string _name;

            public Marker(string name) => _name = name;

            public override string ToString() => "RemovalReason." + _name;
        }
    }

    /// <summary>
    ///     What a route left the stack with: whether it carried a value, the value, and the request that
    ///     led to the pop.
    /// </summary>
    /// <remarks>
    ///     <see cref="HasValue"/> rather than a nullable value: for value types, default is a legitimate
    ///     result. <see cref="Request"/> is null when the route closed itself, and otherwise whatever the
    ///     requester passed to <c>RequestPop</c> or one of the <see cref="RemovalReason"/> markers; the
    ///     navigator never interprets it.
    /// </remarks>
    public readonly struct PopResult
    {
        public bool HasValue { get; }
        public object Value { get; }
        public object Request { get; }

        internal PopResult(bool hasValue, object value, object request)
        {
            HasValue = hasValue;
            Value = value;
            Request = request;
        }

        public void Deconstruct(out bool hasValue, out object value)
        {
            hasValue = HasValue;
            value = Value;
        }

        /// <summary>A result without a value.</summary>
        public static PopResult None(object request = null) => new PopResult(false, null, request);

        /// <summary>A result carrying an untyped value. Typed routes go through <see cref="Of{T}"/>.</summary>
        public static PopResult OfValue(object value, object request = null) => new PopResult(true, value, request);

        public static PopResult<T> Of<T>(T value, object request = null) => new PopResult<T>(true, value, request);

        public static PopResult<T> None<T>(object request = null) => new PopResult<T>(false, default, request);

        public override string ToString() =>
            (HasValue ? "PopResult(" + Value + ")" : "PopResult(none)") + (Request == null ? "" : " by " + Request);
    }

    /// <summary>
    ///     <see cref="PopResult"/> for a <see cref="Route{T}"/>: the value is a <typeparamref name="T"/>,
    ///     by construction rather than by cast.
    /// </summary>
    public readonly struct PopResult<T>
    {
        public bool HasValue { get; }
        public T Value { get; }
        public object Request { get; }

        internal PopResult(bool hasValue, T value, object request)
        {
            HasValue = hasValue;
            Value = value;
            Request = request;
        }

        public void Deconstruct(out bool hasValue, out T value)
        {
            hasValue = HasValue;
            value = Value;
        }

        public override string ToString() =>
            (HasValue ? "PopResult(" + Value + ")" : "PopResult(none)") + (Request == null ? "" : " by " + Request);
    }
}
