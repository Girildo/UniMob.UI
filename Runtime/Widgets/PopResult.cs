namespace UniMob.UI.Widgets
{
    using System;

    /// <summary>
    ///     The requests the navigator itself stamps onto a result when it removes a route without anyone
    ///     asking the route.
    /// </summary>
    /// <remarks>
    ///     A result's <c>Request</c> is null exactly when the route closed itself. Everything else that
    ///     ends a route is a request of some kind, and these are the ones that originate inside the package:
    ///     <see cref="Teardown"/> for the navigator unmounting, an un-asked <c>Replace</c> or <c>NewRoot</c>
    ///     removing the route from under a caller, and <see cref="Back"/> for the back button.
    ///     Callers supply their own request objects for everything they ask; the navigator carries them
    ///     through without looking inside.
    /// </remarks>
    public static class PopRequest
    {
        public static readonly object Teardown = new Marker("Teardown");
        public static readonly object Back = new Marker("Back");

        private sealed class Marker
        {
            private readonly string _name;

            public Marker(string name) => _name = name;

            public override string ToString() => "PopRequest." + _name;
        }
    }

    /// <summary>
    ///     What a route left the stack with: whether it carried a value, the value, and the request that
    ///     led to the pop.
    /// </summary>
    /// <remarks>
    ///     <see cref="HasValue"/> rather than a nullable value, because for value types default is a
    ///     legitimate result and null cannot mean "none". <see cref="Request"/> is null when the route
    ///     closed itself, and otherwise whatever object the requester passed to <c>RequestPop</c> (or one
    ///     of the <see cref="PopRequest"/> markers). The navigator never interprets it.
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
