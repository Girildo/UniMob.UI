namespace UniMob.UI.Navigation
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using UniMob;
    using UniMob.UI.Rendering;
    using UnityEngine;

    public class Navigator : StatefulWidget
    {
        public string InitialRoute { get; }

        public Dictionary<string, Func<Route>> Routes { get; }

        /// <summary>
        ///     Who is told what this navigator does to its stack. May be null.
        /// </summary>
        /// <remarks>
        ///     Configuration, like <see cref="Routes"/>, and read off the current widget the same way, so
        ///     a rebuild that supplies a different set takes effect from the next operation onwards. Each
        ///     operation announces to the set it started with, so no observer hears one edge of an
        ///     operation without the other.
        ///     <para>
        ///         Every observer hears the initial route being pushed: the widget is in place before
        ///         <c>InitState</c> runs, and pushing the initial route is the first thing it does.
        ///     </para>
        /// </remarks>
        public IReadOnlyList<INavigatorObserver> Observers { get; }

        public Navigator(
            string initialRoute,
            Dictionary<string, Func<Route>> routes,
            IReadOnlyList<INavigatorObserver>? observers = null
        )
        {
            InitialRoute = initialRoute;
            Routes = routes;
            Observers = observers ?? Array.Empty<INavigatorObserver>();
        }

        public override State CreateState() => new NavigatorState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state) =>
            new RenderNavigator((INavigatorState)state);

        /// <summary>
        ///     The navigator above <paramref name="context"/>, or the outermost one when
        ///     <paramref name="rootNavigator"/> is set. Throws where there is none; use
        ///     <see cref="OfOrNull"/> where its absence is an answer rather than a fault.
        /// </summary>
        public static NavigatorState Of(BuildContext context, bool rootNavigator = false)
        {
            return OfOrNull(context, rootNavigator)
                ?? throw new Exception(
                    "Navigator operation requested with a context that does not include a Navigator.\n"
                        + "The context used to push or pop routes from the Navigator must be that of a "
                        + "widget that is a descendant of a Navigator widget."
                );
        }

        /// <summary>
        ///     The navigator above <paramref name="context"/>, or null where there is none.
        /// </summary>
        public static NavigatorState? OfOrNull(BuildContext context, bool rootNavigator = false)
        {
            return rootNavigator
                ? context.RootAncestorStateOfType<NavigatorState>()
                : context.AncestorStateOfType<NavigatorState>();
        }

        public static Route Push(BuildContext context, Route route) => Of(context).Push(route);

        public static Route PushNamed(BuildContext context, string routeName) =>
            Of(context).PushNamed(routeName);

        /// <summary>Pops the topmost route on its own authority; the caller is taken to own it.</summary>
        public static Task<PopOutcome> Pop(BuildContext context) => Of(context).TopmostRoute.Pop();

        public static Route NewRoot(BuildContext context, Route route) =>
            Of(context).NewRoot(route);

        public static Route NewRootNamed(BuildContext context, string routeName) =>
            Of(context).NewRootNamed(routeName);

        public static Route Replace(BuildContext context, Route route) =>
            Of(context).Replace(route);

        public static Route ReplaceNamed(BuildContext context, string routeName) =>
            Of(context).ReplaceNamed(routeName);

        public static Task<PopToOutcome> PopTo(BuildContext context, Route route, object request) =>
            Of(context).RequestPopTo(route, request);
    }
}
