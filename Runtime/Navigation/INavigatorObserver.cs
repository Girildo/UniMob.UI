namespace UniMob.UI.Navigation
{
    /// <summary>
    ///     Hears what a <see cref="Navigator"/> does to its stack. Supplied as configuration through
    ///     <see cref="Navigator.Observers"/> and read off the current widget at dispatch, like the route
    ///     table; there is no add/remove pair.
    /// </summary>
    /// <remarks>
    ///     Observers are told, never consulted. Each call is made inside a try/catch: one that throws is
    ///     logged and navigation carries on, and nothing an observer returns changes where the navigator
    ///     goes. Refusing lives on <see cref="Route"/>, through <see cref="NavigatorState.RequestPop"/>;
    ///     a refused request fires no callback, since nothing happened. Navigating from inside a callback
    ///     is safe: the new command is queued behind the one being announced.
    ///     <para>
    ///         An interface, so that an object with a base class of its own can take the role. If a
    ///         member is ever added, add a no-op adapter alongside it.
    ///     </para>
    /// </remarks>
    public interface INavigatorObserver
    {
        /// <summary>
        ///     A push is starting. <paramref name="previousRoute"/> is the route it will cover, or null
        ///     when the navigator is empty. Raised before the incoming route is built, so a push whose
        ///     initialization fails ends without a matching <see cref="DidPush"/>.
        /// </summary>
        void WillPush(Route route, Route? previousRoute);

        /// <summary>
        ///     A push has been committed. The stack already contains <paramref name="route"/>, so
        ///     <c>NavigationStack</c> and <c>TopmostRoute</c> agree with this callback while it runs.
        /// </summary>
        void DidPush(Route route, Route? previousRoute);

        /// <summary>
        ///     A pop is starting. <paramref name="previousRoute"/> is the route that will be revealed
        ///     underneath.
        /// </summary>
        void WillPop(Route route, Route? previousRoute);

        /// <summary>
        ///     A pop has been committed. Reached even when the route's exit transition failed: the
        ///     navigator commits the removal either way, so every <see cref="WillPop"/> is matched.
        /// </summary>
        void DidPop(Route route, Route? previousRoute);

        /// <summary>
        ///     A replace is starting. A replace on an empty navigator announces itself as a push instead,
        ///     since there is no old route to name. Raised before the incoming route is built, like
        ///     <see cref="WillPush"/>, and unmatched on the same terms; a failing outgoing transition can
        ///     also abort it, since a replace does not commit its removal against one.
        /// </summary>
        void WillReplace(Route newRoute, Route? oldRoute);

        /// <summary>
        ///     A replace has been committed.
        /// </summary>
        void DidReplace(Route newRoute, Route? oldRoute);
    }
}
