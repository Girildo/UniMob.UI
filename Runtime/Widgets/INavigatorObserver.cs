namespace UniMob.UI.Widgets
{
    /// <summary>
    ///     Hears what a <see cref="Navigator"/> does to its stack.
    /// </summary>
    /// <remarks>
    ///     Supplied to the navigator as configuration, through <see cref="Navigator.Observers"/>, and read
    ///     off the current widget at dispatch exactly as its route table is. There is no add/remove pair:
    ///     a mutable list of observers beside the widget's own would be two sources of truth to reconcile
    ///     on every rebuild, and something that arrives too late to be configured can subscribe to a
    ///     long-lived observer instead of to the navigator.
    ///     <para>
    ///         An interface rather than a base class with empty bodies, because every framework-consumed
    ///         contract in this package is one and objects take on roles by implementing them --
    ///         <c>Route : IBackActionOwner</c>, <c>NavigatorState : INavigatorState,
    ///         IMultiChildLayoutState</c> -- and a base class cannot be given to an object that already
    ///         has one. If a member is ever added, a no-op adapter belongs alongside it at that moment.
    ///     </para>
    ///     <para>
    ///         Observers are told, never consulted. Each call is made inside a try/catch, so one that
    ///         throws is logged and navigation carries on; nothing an observer does or returns can change
    ///         where the navigator goes. Refusing a navigation is a different mechanism and lives on
    ///         <see cref="Route"/>: <see cref="NavigatorState.RequestPop"/> asks a route through
    ///         <c>Route.OnPopRequested</c> before anything moves, and a refused request fires no
    ///         observer callback at all, since nothing happened.
    ///     </para>
    ///     <para>
    ///         Pushing from inside a callback is safe: the navigator is part-way through its command loop,
    ///         so the new command is queued behind the one being announced rather than re-entering it.
    ///     </para>
    /// </remarks>
    public interface INavigatorObserver
    {
        /// <summary>
        ///     A push is starting. <paramref name="previousRoute"/> is the route it will cover, or null
        ///     when the navigator is empty.
        /// </summary>
        /// <remarks>
        ///     Raised before the incoming route is built, which is the one step of a push that can take
        ///     arbitrarily long: a route that preloads holds this interval open for as long as it needs.
        ///     The cost of drawing the bracket there is that a push whose initialization fails ends
        ///     without a matching <see cref="DidPush"/>.
        /// </remarks>
        void WillPush(Route route, Route previousRoute);

        /// <summary>
        ///     A push has been committed. The stack already contains <paramref name="route"/>, so
        ///     <c>NavigationStack</c> and <c>TopmostRoute</c> agree with this callback while it runs.
        /// </summary>
        void DidPush(Route route, Route previousRoute);

        /// <summary>
        ///     A pop is starting. <paramref name="previousRoute"/> is the route that will be revealed
        ///     underneath.
        /// </summary>
        void WillPop(Route route, Route previousRoute);

        /// <summary>
        ///     A pop has been committed.
        /// </summary>
        /// <remarks>
        ///     Reached even when the route's exit transition failed, because the navigator commits a
        ///     removal either way rather than leaving a route that has already reported itself finished on
        ///     the stack. Every <see cref="WillPop"/> is therefore matched.
        /// </remarks>
        void DidPop(Route route, Route previousRoute);

        /// <summary>
        ///     A replace is starting. A replace on an empty navigator announces itself as a push instead,
        ///     since the depth is known before anything moves and there is no old route to name.
        /// </summary>
        /// <remarks>
        ///     Raised before the incoming route is built, as <see cref="WillPush"/> is, and unmatched on
        ///     the same terms. A replace removes before it adds, so its removal is deliberately not
        ///     committed against a failing transition -- doing so could end the command with an empty
        ///     navigator -- which leaves an abort possible here regardless of where the bracket is drawn.
        /// </remarks>
        void WillReplace(Route newRoute, Route oldRoute);

        /// <summary>
        ///     A replace has been committed.
        /// </summary>
        void DidReplace(Route newRoute, Route oldRoute);
    }
}
