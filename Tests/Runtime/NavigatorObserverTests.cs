using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UniMob.UI.Widgets;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Covers what <see cref="INavigatorObserver"/> promises: which callback fires for which
    ///     operation, with which pair of routes, and that nothing an observer does can change where the
    ///     navigator goes.
    /// </summary>
    /// <remarks>
    ///     Where each callback fires <i>relative to the lifecycle around it</i> is pinned by the golden
    ///     traces in <see cref="NavigatorBaselineTests"/> instead, which record observer callbacks into
    ///     the same sequence as every <c>OnPause</c>, <c>OnDestroy</c> and stack mutation. That is the
    ///     stronger statement and no assertion here tries to repeat it. These fixtures state the contract
    ///     in isolation, so a reader can see what an observer is promised without reading a whole trace.
    ///     <para>
    ///         PlayMode only, for the reason given on <see cref="NavigatorHost"/>.
    ///     </para>
    /// </remarks>
    public class NavigatorObserverTests
    {
        [UnityTest]
        public IEnumerator Mount_AnnouncesTheInitialRouteAsAPushOntoNothing()
        {
            var observer = new RecordingObserver();

            var host = NavigatorHost.Mount(
                "A",
                RouteModalType.Fullscreen,
                RouteFlavour.Plain,
                observer
            );
            yield return host.Settle();

            CollectionAssert.AreEqual(
                new[] { "WillPush(A, null)", "DidPush(A, null)" },
                observer.Calls,
                "an observer supplied at construction hears the initial route, since the widget is in "
                    + "place before InitState pushes it"
            );
        }

        [UnityTest]
        public IEnumerator Push_AnnouncesTheRouteItCovers()
        {
            var observer = new RecordingObserver();

            var host = NavigatorHost.Mount(
                "A",
                RouteModalType.Fullscreen,
                RouteFlavour.Plain,
                observer
            );
            yield return host.Settle();
            observer.Calls.Clear();

            host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();

            CollectionAssert.AreEqual(new[] { "WillPush(B, A)", "DidPush(B, A)" }, observer.Calls);
        }

        [UnityTest]
        public IEnumerator Pop_AnnouncesTheRouteItReveals()
        {
            var observer = new RecordingObserver();

            var host = NavigatorHost.Mount(
                "A",
                RouteModalType.Fullscreen,
                RouteFlavour.Plain,
                observer
            );
            yield return host.Settle();

            host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();
            observer.Calls.Clear();

            host.Navigator.TopmostRoute.Pop();
            yield return host.Settle();

            CollectionAssert.AreEqual(new[] { "WillPop(B, A)", "DidPop(B, A)" }, observer.Calls);
        }

        /// <summary>
        ///     A PopTo is one operation that removes several routes, and each removal is announced with
        ///     the route it would reveal rather than with the eventual destination.
        /// </summary>
        [UnityTest]
        public IEnumerator PopTo_AnnouncesEachRemovalWithTheRouteBeneathIt()
        {
            var observer = new RecordingObserver();

            var host = NavigatorHost.Mount(
                "A",
                RouteModalType.Fullscreen,
                RouteFlavour.Plain,
                observer
            );
            yield return host.Settle();

            var root = host.Navigator.TopmostRoute;

            host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();

            host.Navigator.Push(host.Create("C", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();
            observer.Calls.Clear();

            host.Navigator.RequestPopTo(root, "test");
            yield return host.Settle();

            CollectionAssert.AreEqual(
                new[] { "WillPop(C, B)", "DidPop(C, B)", "WillPop(B, A)", "DidPop(B, A)" },
                observer.Calls
            );
        }

        [UnityTest]
        public IEnumerator Replace_AnnouncesBothRoutes()
        {
            var observer = new RecordingObserver();

            var host = NavigatorHost.Mount(
                "A",
                RouteModalType.Fullscreen,
                RouteFlavour.Plain,
                observer
            );
            yield return host.Settle();
            observer.Calls.Clear();

            host.Navigator.Replace(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();

            CollectionAssert.AreEqual(
                new[] { "WillReplace(B, A)", "DidReplace(B, A)" },
                observer.Calls
            );
        }

        /// <summary>
        ///     A replace with nothing to replace announces a push, since the depth is settled before
        ///     anything moves and there is no old route to name.
        /// </summary>
        /// <remarks>
        ///     Reached by destroying the navigator's whole stack first, which is the only way to empty it:
        ///     Pop and PopTo both refuse below depth one, and Replace itself always leaves a route behind.
        ///     <c>NavigatorState.ApplyScreenEvent(Destroy)</c> pops directly rather than through
        ///     <c>PopInternal</c>, so it announces nothing on its way -- the navigator is being ended
        ///     wholesale there, which is what a route's own lifecycle channel reports.
        /// </remarks>
        [UnityTest]
        public IEnumerator Replace_OnAnEmptyNavigator_AnnouncesAPush()
        {
            var observer = new RecordingObserver();

            var host = NavigatorHost.Mount(
                "A",
                RouteModalType.Fullscreen,
                RouteFlavour.Plain,
                observer
            );
            yield return host.Settle();

            _ = host.Navigator.ApplyScreenEvent(ScreenEvent.Destroy);
            yield return host.Settle();

            Assert.AreEqual(
                0,
                host.Navigator.NavigationStack.Count,
                "the stack must be empty for this to mean anything"
            );
            observer.Calls.Clear();

            host.Navigator.Replace(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();

            CollectionAssert.AreEqual(
                new[] { "WillPush(B, null)", "DidPush(B, null)" },
                observer.Calls
            );
        }

        /// <summary>
        ///     A pop commits even when the route's exit transition fails, so its Will is still matched.
        /// </summary>
        [UnityTest]
        public IEnumerator Pop_WhenTheTransitionFails_StillAnnouncesTheCommit()
        {
            // The failing destroy reaches ProcessCommandsLoop, which logs it.
            LogAssert.ignoreFailingMessages = true;

            var observer = new RecordingObserver();

            var host = NavigatorHost.Mount(
                "A",
                RouteModalType.Fullscreen,
                RouteFlavour.Plain,
                observer
            );
            yield return host.Settle();

            host.Navigator.Push(
                host.Create("B", RouteModalType.Fullscreen, RouteFlavour.ThrowsAsyncOnDestroy)
            );
            yield return host.Settle();
            observer.Calls.Clear();

            host.Navigator.TopmostRoute.Pop();
            yield return host.Settle();

            CollectionAssert.AreEqual(
                new[] { "WillPop(B, A)", "DidPop(B, A)" },
                observer.Calls,
                "the removal commits against a failed transition, so the notification does too"
            );
        }

        /// <summary>
        ///     One observer throwing must not stop the navigator, nor deprive the others of the callback.
        /// </summary>
        [UnityTest]
        public IEnumerator AThrowingObserver_NeitherStopsNavigationNorSilencesTheOthers()
        {
            // Every contained observer exception is logged, which is the behaviour under test.
            LogAssert.ignoreFailingMessages = true;

            var throwing = new ThrowingObserver();
            var listening = new RecordingObserver();

            var host = NavigatorHost.Mount(
                "A",
                RouteModalType.Fullscreen,
                RouteFlavour.Plain,
                throwing,
                listening
            );
            yield return host.Settle();
            listening.Calls.Clear();

            host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();

            host.Navigator.TopmostRoute.Pop();
            yield return host.Settle();

            Assert.AreEqual(
                1,
                host.Navigator.NavigationStack.Count,
                "navigation ran to completion"
            );
            Assert.AreEqual("A", host.Navigator.TopmostRoute.Key);

            CollectionAssert.AreEqual(
                new[] { "WillPush(B, A)", "DidPush(B, A)", "WillPop(B, A)", "DidPop(B, A)" },
                listening.Calls,
                "an observer earlier in the list throwing must not cost a later one its callbacks"
            );
        }

        /// <summary>
        ///     An observer that unregisters itself mid-operation does not corrupt the dispatch it is
        ///     inside, because dispatch walks the snapshot the operation began with.
        /// </summary>
        [UnityTest]
        public IEnumerator AnObserverRemovedMidOperation_StillHearsThatOperationOut()
        {
            var listening = new RecordingObserver();
            NavigatorHost host = null;

            // Only on the push under test: at mount the host does not exist yet, and the point is to
            // replace the observer list part-way through an operation that is already announcing.
            var removing = new CallbackObserver(onWillPush: route =>
            {
                if (route.Key == "B")
                {
                    host.Rebuild();
                }
            });

            host = NavigatorHost.Mount(
                "A",
                RouteModalType.Fullscreen,
                RouteFlavour.Plain,
                removing,
                listening
            );
            yield return host.Settle();
            listening.Calls.Clear();

            host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();

            CollectionAssert.AreEqual(
                new[] { "WillPush(B, A)", "DidPush(B, A)" },
                listening.Calls,
                "the operation announces to the set it started with, even after the widget was replaced "
                    + "part-way through it"
            );
        }

        /// <summary>
        ///     Observers come from whichever widget is current, so a rebuild that supplies a different set
        ///     takes effect from the next operation.
        /// </summary>
        [UnityTest]
        public IEnumerator ObserversFromARebuiltWidget_ReplaceTheOnesItWasMountedWith()
        {
            var mounted = new RecordingObserver();
            var rebuilt = new RecordingObserver();

            var host = NavigatorHost.Mount(
                "A",
                RouteModalType.Fullscreen,
                RouteFlavour.Plain,
                mounted
            );
            yield return host.Settle();

            host.Rebuild(rebuilt);
            mounted.Calls.Clear();

            host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();

            CollectionAssert.AreEqual(
                new[] { "WillPush(B, A)", "DidPush(B, A)" },
                rebuilt.Calls,
                "the observer the rebuild supplied hears the push"
            );
            CollectionAssert.IsEmpty(
                mounted.Calls,
                "the observer it replaced hears nothing further"
            );
        }

        /// <summary>
        ///     Pushing from inside a callback is queued behind the operation being announced rather than
        ///     re-entering it.
        /// </summary>
        /// <remarks>
        ///     The evidence is the interleaving, not the order of the callbacks, so this one is a trace:
        ///     if the nested push re-entered, C would initialize before B was ever created. The command
        ///     loop is what makes it safe -- <c>ApplyCommands</c> only starts a loop when none is running,
        ///     so a command raised from inside one joins its queue.
        /// </remarks>
        [UnityTest]
        public IEnumerator PushingFromInsideACallback_IsQueuedBehindTheOperationThatCausedIt()
        {
            NavigatorHost host = null;

            var pushing = new CallbackObserver(onDidPush: route =>
            {
                if (route.Key == "B")
                {
                    host.Navigator.Push(
                        host.Create("C", RouteModalType.Fullscreen, RouteFlavour.Plain)
                    );
                }
            });

            host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain, pushing);
            yield return host.Settle();
            host.End();

            host.Begin("push B, whose DidPush pushes C");
            host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();
            host.End();

            host.AssertTrace(
                "mount A",
                "  > WillPush(A, null)",
                "  A OnInitialize",
                "  > DidPush(A, null)",
                "  A OnCreate",
                "  A OnResume",
                "  A OnFocus",
                "  stack: [A]",
                "push B, whose DidPush pushes C",
                "  > WillPush(B, A)",
                "  B OnInitialize",
                "  A OnFocusLost",
                "  A OnPause",
                "  > DidPush(B, A)",
                "  B OnCreate",
                "  B OnResume",
                "  B OnFocus",
                "  > WillPush(C, B)",
                "  C OnInitialize",
                "  B OnFocusLost",
                "  B OnPause",
                "  > DidPush(C, B)",
                "  C OnCreate",
                "  C OnResume",
                "  C OnFocus",
                "  stack: [C, B, A]"
            );
        }

        /// <summary>
        ///     Observing must not perturb what is observed: an observer reading the navigator's atoms from
        ///     inside its callback must not graft that dependency onto whoever started the navigation.
        /// </summary>
        /// <remarks>
        ///     A navigation whose handlers all complete synchronously -- every one that does not animate --
        ///     runs to its last callback while still on the stack of whatever started it. If that was a
        ///     computation, then an observer reading the incoming route's <c>ScreenState</c>, which is the
        ///     obvious thing for one to read, would leave the computation depending on that route, and the
        ///     next push -- which pauses it -- would re-run the computation. Something re-running because
        ///     something else was listening is the one effect a listener must never have.
        ///     <para>
        ///         <b>The setup is deliberately abnormal.</b> Navigating from a watched scope is already
        ///         reported by UniMob as dangerous, because navigation invalidates atoms and
        ///         <c>AtomBase.Invalidate</c> logs whenever it runs inside one -- which is why this fixture
        ///         suppresses log assertions. That is exactly why it is worth pinning: the report is a
        ///         logged error, not a stop, so execution continues. Untracked dispatch is what stops a
        ///         second, silent failure being laid on top of the loud one.
        ///     </para>
        ///     <para>
        ///         The rebuild at the end covers the other half, which needs no observer to misbehave at
        ///         all: the observer list is read off the widget, and the widget is itself an atom, so
        ///         notifying would otherwise leave the caller depending on it whether or not any observer
        ///         read anything.
        ///     </para>
        /// </remarks>
        [UnityTest]
        public IEnumerator AnObserverReadingRouteState_DoesNotMakeTheCallerDependOnIt()
        {
            // Both the reaction's push and the later ones invalidate atoms while a watched scope is
            // active, which UniMob reports. See the remarks: that is the premise, not a side effect.
            LogAssert.ignoreFailingMessages = true;

            var host = NavigatorHost.Mount(
                "A",
                RouteModalType.Fullscreen,
                RouteFlavour.Plain,
                new StateReadingObserver()
            );
            yield return host.Settle();

            var runs = 0;
            var unrelated = Atom.Value(0);
            var lifetime = new LifetimeController();

            try
            {
                Atom.Reaction(
                    lifetime.Lifetime,
                    () =>
                    {
                        runs++;

                        // A real dependency, so the reaction is a genuine computation rather than one that
                        // could never re-run for want of anything to watch.
                        _ = unrelated.Value;

                        if (runs == 1)
                        {
                            host.Navigator.Push(
                                host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain)
                            );
                        }
                    }
                );

                yield return host.Settle();

                Assert.AreEqual(
                    1,
                    runs,
                    "the reaction has run once, and pushed from inside that run"
                );

                host.Navigator.Push(
                    host.Create("C", RouteModalType.Fullscreen, RouteFlavour.Plain)
                );
                yield return host.Settle();

                Assert.AreEqual(
                    1,
                    runs,
                    "a push the reaction had nothing to do with must not re-run it: the observer's read of B's "
                        + "ScreenState must not have been recorded against the reaction"
                );

                host.Rebuild();
                yield return host.Settle();

                Assert.AreEqual(
                    1,
                    runs,
                    "nor may reading the observer list off the widget leave the reaction depending on it"
                );
            }
            finally
            {
                lifetime.Dispose();
            }
        }

        /// <summary>
        ///     Reads the navigator's reactive state from inside a callback, which is the ordinary thing for
        ///     an observer to do and the thing that must stay invisible to whoever started the navigation.
        /// </summary>
        private sealed class StateReadingObserver : INavigatorObserver
        {
            public void WillPush(Route route, Route previousRoute) => Read(route);

            public void DidPush(Route route, Route previousRoute) => Read(route);

            public void WillPop(Route route, Route previousRoute) => Read(route);

            public void DidPop(Route route, Route previousRoute) => Read(route);

            public void WillReplace(Route newRoute, Route oldRoute) => Read(newRoute);

            public void DidReplace(Route newRoute, Route oldRoute) => Read(newRoute);

            private static void Read(Route route)
            {
                _ = route.ScreenState;
            }
        }

        /// <summary>
        ///     Records each callback as <c>Name(route, otherRoute)</c>, which is enough to state both which
        ///     callback fired and which pair of routes it named.
        /// </summary>
        private sealed class RecordingObserver : INavigatorObserver
        {
            public List<string> Calls { get; } = new List<string>();

            public void WillPush(Route route, Route previousRoute) =>
                Record("WillPush", route, previousRoute);

            public void DidPush(Route route, Route previousRoute) =>
                Record("DidPush", route, previousRoute);

            public void WillPop(Route route, Route previousRoute) =>
                Record("WillPop", route, previousRoute);

            public void DidPop(Route route, Route previousRoute) =>
                Record("DidPop", route, previousRoute);

            public void WillReplace(Route newRoute, Route oldRoute) =>
                Record("WillReplace", newRoute, oldRoute);

            public void DidReplace(Route newRoute, Route oldRoute) =>
                Record("DidReplace", newRoute, oldRoute);

            private void Record(string callback, Route route, Route other)
            {
                Calls.Add(callback + "(" + Name(route) + ", " + Name(other) + ")");
            }

            private static string Name(Route route) => route == null ? "null" : route.Key;
        }

        private sealed class ThrowingObserver : INavigatorObserver
        {
            public void WillPush(Route route, Route previousRoute) =>
                throw new InvalidOperationException("observer failed");

            public void DidPush(Route route, Route previousRoute) =>
                throw new InvalidOperationException("observer failed");

            public void WillPop(Route route, Route previousRoute) =>
                throw new InvalidOperationException("observer failed");

            public void DidPop(Route route, Route previousRoute) =>
                throw new InvalidOperationException("observer failed");

            public void WillReplace(Route newRoute, Route oldRoute) =>
                throw new InvalidOperationException("observer failed");

            public void DidReplace(Route newRoute, Route oldRoute) =>
                throw new InvalidOperationException("observer failed");
        }

        /// <summary>
        ///     Does something back to the navigator from inside a callback, which is the case dispatch has
        ///     to survive.
        /// </summary>
        private sealed class CallbackObserver : INavigatorObserver
        {
            private readonly Action<Route> _onWillPush;
            private readonly Action<Route> _onDidPush;

            public CallbackObserver(Action<Route> onWillPush = null, Action<Route> onDidPush = null)
            {
                _onWillPush = onWillPush;
                _onDidPush = onDidPush;
            }

            public void WillPush(Route route, Route previousRoute) => _onWillPush?.Invoke(route);

            public void DidPush(Route route, Route previousRoute) => _onDidPush?.Invoke(route);

            public void WillPop(Route route, Route previousRoute) { }

            public void DidPop(Route route, Route previousRoute) { }

            public void WillReplace(Route newRoute, Route oldRoute) { }

            public void DidReplace(Route newRoute, Route oldRoute) { }
        }
    }
}
