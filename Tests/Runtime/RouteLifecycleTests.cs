using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UniMob.UI.Widgets;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Covers the two channels a <see cref="Route"/> publishes its own lifecycle on: the state, as an
    ///     atom, and the events that reached it.
    /// </summary>
    /// <remarks>
    ///     They are not redundant. The state answers "where is this route now", which used to be
    ///     answerable only by polling a plain getter. The events answer "how did it get there", which the
    ///     state cannot: the two ways a route can end, navigation removing it and the tree it lived in
    ///     going away, both land on <see cref="ScreenState.Destroyed"/>.
    ///     <para>
    ///         PlayMode only. Route completion defers through <c>Zone.NextFrame</c> and reactions are
    ///         actualized by <c>AtomScheduler</c> on the following frame, neither of which exists outside
    ///         play mode.
    ///     </para>
    /// </remarks>
    public class RouteLifecycleTests
    {
        /// <summary>
        ///     A reaction over <see cref="Route.ScreenState"/> follows a route through being pushed,
        ///     covered, uncovered and popped, without anything polling it.
        /// </summary>
        /// <remarks>
        ///     Reactions are actualized once per frame, so what a reaction sees is the state at each frame
        ///     boundary rather than every transition the machine passed through. That is the granularity
        ///     any consumer gets, which is why the fixture asserts at that granularity rather than
        ///     inspecting the atom directly.
        /// </remarks>
        [UnityTest]
        public IEnumerator ScreenState_IsFollowedByAReaction_ThroughAFullCycle()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var watched = host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain);
            var seen = new List<ScreenState>();
            var lifetime = new LifetimeController();

            try
            {
                Atom.Reaction(lifetime.Lifetime, () => watched.ScreenState, state => seen.Add(state));

                host.Navigator.Push(watched);
                yield return host.Settle();

                host.Navigator.Push(host.Create("C", RouteModalType.Fullscreen, RouteFlavour.Plain));
                yield return host.Settle();

                host.Navigator.Pop();
                yield return host.Settle();

                host.Navigator.Pop();
                yield return host.Settle();
            }
            finally
            {
                lifetime.Dispose();
            }

            CollectionAssert.AreEqual(
                new[]
                {
                    ScreenState.Initializing,
                    ScreenState.Focused,
                    ScreenState.Created,
                    ScreenState.Focused,
                    ScreenState.Destroyed,
                },
                seen,
                "pushed, covered by C, uncovered when C popped, then popped itself");
        }

        /// <summary>
        ///     An event the machine chains onwards is reported as the several transitions it is, each
        ///     paired with the state that transition reached.
        /// </summary>
        /// <remarks>
        ///     Focusing a merely created route is the case a naive implementation gets wrong. It is two
        ///     transitions -- Created to Resumed running <c>OnResume</c>, then Resumed to Focused running
        ///     <c>OnFocus</c> -- and a publisher wrapped around the outermost trigger would report one
        ///     event, after both had already run, with the state they ended at. Pairing each event with
        ///     the state read from inside the callback is what states the difference.
        /// </remarks>
        [UnityTest]
        public IEnumerator ScreenEventApplied_ReportsAChainedEventOncePerTransition()
        {
            var trace = new NavigatorTrace();
            var route = new TracingRoute(trace, "R", RouteModalType.Fullscreen);
            var applied = new List<string>();

            route.ScreenEventApplied += screenEvent => applied.Add(screenEvent + " -> " + route.ScreenState);

            route.ApplyScreenEvent(ScreenEvent.Create);
            route.ApplyScreenEvent(ScreenEvent.Focus);

            yield return null;

            CollectionAssert.AreEqual(
                new[] { "Create -> Created", "Focus -> Resumed", "Focus -> Focused" },
                applied,
                "one firing per transition, in the order the machine took them");

            Assert.AreEqual(
                "  R OnCreate\n  R OnResume\n  R OnFocus",
                trace.ToString(),
                "and the handlers those transitions run are unchanged");
        }

        /// <summary>
        ///     The two endings are told apart by the event, which is the whole reason the event channel
        ///     exists beside the state one.
        /// </summary>
        [UnityTest]
        public IEnumerator DestroyAndTeardown_ReachTheSameStateAndReportDifferentCauses()
        {
            var trace = new NavigatorTrace();

            var removed = new TracingRoute(trace, "removed", RouteModalType.Fullscreen);
            var tornDown = new TracingRoute(trace, "torn-down", RouteModalType.Fullscreen);

            var removedEvents = new List<ScreenEvent>();
            var tornDownEvents = new List<ScreenEvent>();

            removed.ScreenEventApplied += removedEvents.Add;
            tornDown.ScreenEventApplied += tornDownEvents.Add;

            removed.ApplyScreenEvent(ScreenEvent.Create);
            removed.ApplyScreenEvent(ScreenEvent.Destroy);

            tornDown.ApplyScreenEvent(ScreenEvent.Create);
            tornDown.ApplyScreenEvent(ScreenEvent.Teardown);

            yield return null;

            Assert.AreEqual(ScreenState.Destroyed, removed.ScreenState);
            Assert.AreEqual(ScreenState.Destroyed, tornDown.ScreenState,
                "the state cannot tell the two endings apart");

            CollectionAssert.AreEqual(
                new[] { ScreenEvent.Create, ScreenEvent.Destroy },
                removedEvents);
            CollectionAssert.AreEqual(
                new[] { ScreenEvent.Create, ScreenEvent.Teardown },
                tornDownEvents,
                "the event channel can");
        }

        /// <summary>
        ///     A route the navigator is unmounting reports Teardown, which is the ending an app most needs
        ///     to distinguish and the one no navigator callback announces.
        /// </summary>
        /// <remarks>
        ///     Deliberately not on <see cref="INavigatorObserver"/>: teardown is per-route and reaches
        ///     every route at once, so it belongs on the channel that is already per-route.
        /// </remarks>
        [UnityTest]
        public IEnumerator Unmount_ReportsTeardownOnEveryLiveRoute()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var root = host.Navigator.TopmostRoute;
            var pushed = host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain);

            host.Navigator.Push(pushed);
            yield return host.Settle();

            var endings = new List<string>();

            root.ScreenEventApplied += screenEvent => Record(endings, "A", screenEvent);
            pushed.ScreenEventApplied += screenEvent => Record(endings, "B", screenEvent);

            host.Unmount();
            yield return host.PumpFrames(3);

            CollectionAssert.AreEqual(
                new[] { "B Teardown", "A Teardown" },
                endings,
                "topmost first, in the order the navigator tears its stack down");
        }

        /// <summary>
        ///     A subscriber that throws is contained, so the transition it was told about still completes
        ///     and the subscribers after it still hear about it.
        /// </summary>
        /// <remarks>
        ///     Uncontained, the throw would escape through <c>Trigger</c> into whichever handler triggered
        ///     the machine and abort a navigation half way, which is a much larger consequence than the
        ///     equivalent on the observer channel. Same treatment either way.
        /// </remarks>
        [UnityTest]
        public IEnumerator ScreenEventApplied_ContainsASubscriberThatThrows()
        {
            LogAssert.ignoreFailingMessages = true;

            var trace = new NavigatorTrace();
            var route = new TracingRoute(trace, "R", RouteModalType.Fullscreen);
            var heard = new List<ScreenEvent>();

            route.ScreenEventApplied += _ => throw new InvalidOperationException("subscriber failed");
            route.ScreenEventApplied += heard.Add;

            route.ApplyScreenEvent(ScreenEvent.Create);
            route.ApplyScreenEvent(ScreenEvent.Destroy);

            yield return null;

            Assert.AreEqual(ScreenState.Destroyed, route.ScreenState, "the transitions still happened");
            Assert.IsTrue(route.PopTask.IsCompleted, "and the destroy still ended the route");

            CollectionAssert.AreEqual(
                new[] { ScreenEvent.Create, ScreenEvent.Destroy },
                heard,
                "the subscriber after the failing one still heard both");
        }

        /// <summary>
        ///     A subscriber reading <see cref="Route.ScreenState"/> from inside its callback must not leave
        ///     whoever drove the transition depending on that route.
        /// </summary>
        /// <remarks>
        ///     Reading the state is the obvious thing for a subscriber to do -- it is the atom this very
        ///     notification was published alongside -- and transitions run on the stack of whatever called
        ///     <c>ApplyScreenEvent</c>. Untracked dispatch is the only thing between those two facts and a
        ///     computation that silently starts re-running for every later transition, because something
        ///     was listening.
        ///     <para>
        ///         <b>The setup is deliberately abnormal</b>, and log assertions are suppressed for it:
        ///         driving a transition from a watched scope invalidates an atom inside one, which UniMob
        ///         reports as dangerous on its own. It reports it and carries on, which is the point --
        ///         the loud failure is not a reason to also allow a silent one.
        ///     </para>
        /// </remarks>
        [UnityTest]
        public IEnumerator ScreenEventApplied_DoesNotLeakTheRoutesStateIntoTheCaller()
        {
            // The transition driven from inside the reaction writes ScreenState while a watched scope is
            // active, which UniMob reports. See the remarks: that is the premise, not a side effect.
            LogAssert.ignoreFailingMessages = true;

            var trace = new NavigatorTrace();
            var route = new TracingRoute(trace, "R", RouteModalType.Fullscreen);

            // Reads the state the notification was published alongside, which is what a real subscriber
            // wanting to act on where the route now is would do.
            route.ScreenEventApplied += screenEvent => { _ = route.ScreenState; };

            var runs = 0;
            var unrelated = Atom.Value(0);
            var lifetime = new LifetimeController();

            try
            {
                Atom.Reaction(lifetime.Lifetime, () =>
                {
                    runs++;
                    _ = unrelated.Value;

                    if (runs == 1)
                    {
                        route.ApplyScreenEvent(ScreenEvent.Create);
                    }
                });

                yield return null;
                yield return null;

                Assert.AreEqual(1, runs, "the reaction has run once, and drove a transition from inside it");

                route.ApplyScreenEvent(ScreenEvent.Destroy);

                yield return null;
                yield return null;

                Assert.AreEqual(1, runs,
                    "a later transition the reaction had nothing to do with must not re-run it");
            }
            finally
            {
                lifetime.Dispose();
            }
        }

        private static void Record(List<string> endings, string key, ScreenEvent screenEvent)
        {
            if (screenEvent == ScreenEvent.Teardown || screenEvent == ScreenEvent.Destroy)
            {
                endings.Add(key + " " + screenEvent);
            }
        }
    }
}
