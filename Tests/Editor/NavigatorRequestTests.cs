using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UniMob.UI.Navigation;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     A route that cannot be built: its initialization throws, which is the one step of a push or
    ///     replace that is supplied by the route and can fail before anything on the stack is touched.
    /// </summary>
    internal sealed class FailingToInitializeRoute : Route
    {
        public FailingToInitializeRoute(string key)
            : base(new RouteSettings(key, RouteModalType.Popup)) { }

        public override Widget Build(BuildContext context) => new Empty();

        protected override Task OnInitialize() =>
            throw new InvalidOperationException("Route '" + Key + "' cannot be initialized.");
    }

    /// <summary>
    ///     A route that answers <see cref="Route.OnPopRequested"/> with whatever the fixture supplies, and
    ///     counts how often it was asked. Its own chrome's way of closing it, the protected
    ///     <see cref="Route.RequestPop"/>, is exposed as <see cref="AskToLeave"/> so a test can stand in
    ///     for that chrome.
    /// </summary>
    internal sealed class DecidingRoute : Route
    {
        private readonly Func<object, Task<PopDecision>> _decide;

        public DecidingRoute(
            string key,
            RouteModalType modalType,
            Func<object, Task<PopDecision>> decide
        )
            : base(new RouteSettings(key, modalType))
        {
            _decide = decide;
        }

        public int TimesAsked { get; private set; }

        public object LastRequest { get; private set; }

        public override Widget Build(BuildContext context) => new Empty();

        public Task<PopOutcome> AskToLeave(object request) => RequestPop(request);

        protected override Task<PopDecision> OnPopRequested(object request)
        {
            TimesAsked++;
            LastRequest = request;
            return _decide(request);
        }
    }

    /// <summary>
    ///     Pins the close protocol: a request asks the route, the route decides, the pop is committed only
    ///     if the route is still on top, and everything the route did not agree to is reported as such.
    /// </summary>
    public class NavigatorRequestTests : NavigatorFixture
    {
        private static Task<PopDecision> Allow(object _) => Task.FromResult(PopDecision.Allow());

        private static Task<PopDecision> Refuse(object _) => Task.FromResult(PopDecision.Refuse());

        [Test]
        public void RequestPop_WhenTheRouteAllows_PopsItCarryingTheRequest()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var route = new DecidingRoute("D", RouteModalType.Popup, Allow);
            host.Navigator.Push(route);
            host.Settle();

            var request = new object();
            var outcome = host.Navigator.RequestPop(route, request);
            host.Settle();

            Assert.AreEqual(PopOutcome.Popped, outcome.Result);
            Assert.AreEqual(1, route.TimesAsked);
            Assert.AreSame(
                request,
                route.LastRequest,
                "the route is handed the request as it was given"
            );
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);

            Assert.IsTrue(route.PopTask.IsCompleted);
            Assert.IsFalse(
                route.PopTask.Result.HasValue,
                "an untyped route cannot attach a value to its answer"
            );
            Assert.AreSame(
                request,
                route.PopTask.Result.Request,
                "the result names the request that popped it"
            );
        }

        [Test]
        public void RequestPop_WhenTheRouteRefuses_LeavesItInPlace()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var route = new DecidingRoute("D", RouteModalType.Popup, Refuse);
            host.Navigator.Push(route);
            host.Settle();

            var outcome = host.Navigator.RequestPop(route, "why");
            host.Settle();

            Assert.AreEqual(PopOutcome.Refused, outcome.Result);
            Assert.AreSame(route, host.Navigator.TopmostRoute);
            Assert.IsFalse(
                route.PopTask.IsCompleted,
                "a refused route has not left, so nothing awaiting it resumes"
            );
        }

        [Test]
        public void RequestPop_ForARouteThatIsNotOnTop_DoesNothingAndSaysSo()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var root = host.Navigator.TopmostRoute;
            var top = new DecidingRoute("D", RouteModalType.Popup, Allow);
            host.Navigator.Push(top);
            host.Settle();

            var outcome = host.Navigator.RequestPop(root, "why");
            host.Settle();

            Assert.AreEqual(PopOutcome.NotTopmost, outcome.Result);
            Assert.AreEqual(2, host.Navigator.NavigationStack.Count, "nothing moved");
            Assert.AreSame(top, host.Navigator.TopmostRoute);
        }

        [Test]
        public void RequestPop_ForTheLastRoute_ReportsLastRoute()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var root = host.Navigator.TopmostRoute;
            var outcome = host.Navigator.RequestPop(root, "why");
            host.Settle();

            Assert.AreEqual(PopOutcome.LastRoute, outcome.Result);
            Assert.AreSame(root, host.Navigator.TopmostRoute);
        }

        [Test]
        public void RequestPop_WhileTheRouteIsStillDeciding_IsAskedOnceAndSharesTheOutcome()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var decision = new TaskCompletionSource<PopDecision>();
            var route = new DecidingRoute("D", RouteModalType.Popup, _ => decision.Task);
            host.Navigator.Push(route);
            host.Settle();

            var first = host.Navigator.RequestPop(route, "first");
            var second = host.Navigator.RequestPop(route, "second");
            host.Settle();

            Assert.IsFalse(first.IsCompleted, "the route has not answered yet");
            Assert.AreEqual(1, route.TimesAsked, "the second requester joins the pending question");
            Assert.AreSame(first, second, "one pending answer, shared");

            decision.SetResult(PopDecision.Allow());
            host.Settle();

            Assert.AreEqual(PopOutcome.Popped, first.Result);
            Assert.AreEqual(PopOutcome.Popped, second.Result);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
            Assert.AreEqual(
                "first",
                route.PopTask.Result.Request,
                "the pop carries the request that started the question"
            );
        }

        [Test]
        public void RequestPop_WhenTheDecisionNavigates_DoesNotDeadlock()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            // The route answers by pushing a "confirmation" and waiting for it, which is only possible
            // because the decision runs outside the command loop.
            Route confirmation = null;
            var route = new DecidingRoute(
                "D",
                RouteModalType.Popup,
                async _ =>
                {
                    confirmation = host.Create("Confirm", RouteModalType.Popup, RouteFlavour.Plain);
                    host.Navigator.Push(confirmation);
                    await confirmation.PopTask;
                    return PopDecision.Allow();
                }
            );
            host.Navigator.Push(route);
            host.Settle();

            var outcome = host.Navigator.RequestPop(route, "why");
            host.Settle();

            Assert.IsFalse(outcome.IsCompleted);
            Assert.AreSame(
                confirmation,
                host.Navigator.TopmostRoute,
                "the confirmation is up while D decides"
            );

            confirmation.Pop();
            host.Settle();

            Assert.AreEqual(PopOutcome.Popped, outcome.Result);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }

        /// <summary>
        ///     The two freedoms the protocol grants have to hold together: a route may navigate while it
        ///     decides, and a second requester shares the pending answer. A route behind the dialog it
        ///     pushed is not on top, and that must not turn the second requester away.
        /// </summary>
        [Test]
        public void RequestPop_WhileTheRouteDecidesBehindItsOwnDialog_StillSharesTheOutcome()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            Route confirmation = null;
            var route = new DecidingRoute(
                "D",
                RouteModalType.Popup,
                async _ =>
                {
                    confirmation = host.Create("Confirm", RouteModalType.Popup, RouteFlavour.Plain);
                    host.Navigator.Push(confirmation);
                    await confirmation.PopTask;
                    return PopDecision.Allow();
                }
            );
            host.Navigator.Push(route);
            host.Settle();

            var first = host.Navigator.RequestPop(route, "first");
            host.Settle();

            Assert.AreSame(
                confirmation,
                host.Navigator.TopmostRoute,
                "D is covered by the dialog it pushed to decide"
            );

            var second = host.Navigator.RequestPop(route, "second");

            Assert.AreSame(
                first,
                second,
                "the second requester joins the question in progress, covered or not"
            );
            Assert.IsFalse(second.IsCompleted, "and is not turned away with NotTopmost");
            Assert.AreEqual(1, route.TimesAsked);

            confirmation.Pop();
            host.Settle();

            Assert.AreEqual(PopOutcome.Popped, first.Result);
            Assert.AreEqual(PopOutcome.Popped, second.Result);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }

        /// <summary>
        ///     The route's slot is taken before the route is asked, not once the asking has returned. The
        ///     hook runs synchronously up to its first await, and a request for the same route issued
        ///     inside that window -- here from the hook itself; an observer of a push the hook makes is the
        ///     realistic source -- must find the question in progress rather than start a competing one.
        /// </summary>
        [Test]
        public void RequestPop_IssuedAgainFromInsideTheHookBeforeItYields_JoinsRatherThanAsksAgain()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var decision = new TaskCompletionSource<PopDecision>();
            Task<PopOutcome> inner = null;
            DecidingRoute route = null;
            route = new DecidingRoute(
                "D",
                RouteModalType.Popup,
                _ =>
                {
                    // Only from the first question. Keyed on TimesAsked, which is counted before the hook
                    // runs, rather than on whether the inner request has been made: a regression asks again
                    // from inside that very call, before its result is assigned, and a guard on the result
                    // would let it nest until the stack ran out instead of failing the assertion below.
                    if (route.TimesAsked == 1)
                    {
                        inner = host.Navigator.RequestPop(route, "inner");
                    }

                    return decision.Task;
                }
            );
            host.Navigator.Push(route);
            host.Settle();

            var outer = host.Navigator.RequestPop(route, "outer");

            Assert.AreEqual(
                1,
                route.TimesAsked,
                "the request from inside the hook joined the question in progress"
            );
            Assert.AreSame(outer, inner, "one pending answer, shared");
            Assert.IsFalse(outer.IsCompleted);

            decision.SetResult(PopDecision.Allow());
            host.Settle();

            Assert.AreEqual(PopOutcome.Popped, outer.Result);
            Assert.AreEqual(PopOutcome.Popped, inner.Result);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }

        [Test]
        public void RequestPop_WhenTheRoutePopsItselfWhileDeciding_ReportsNotTopmost()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var decision = new TaskCompletionSource<PopDecision>();
            var route = new DecidingRoute("D", RouteModalType.Popup, _ => decision.Task);
            host.Navigator.Push(route);
            host.Settle();

            var outcome = host.Navigator.RequestPop(route, "why");
            host.Settle();

            // Self is authoritative: the route's own pop lands now, whatever question is pending.
            var own = route.Pop();
            host.Settle();

            Assert.AreEqual(PopOutcome.Popped, own.Result);
            Assert.IsNull(route.PopTask.Result.Request, "the route closed itself");

            decision.SetResult(PopDecision.Allow());
            host.Settle();

            Assert.AreEqual(
                PopOutcome.NotTopmost,
                outcome.Result,
                "the request finds its route already gone"
            );
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }

        [Test]
        public void RequestPop_WhenTheDecisionPopsTheRouteItself_QueuesRatherThanReenters()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            DecidingRoute route = null;
            route = new DecidingRoute(
                "D",
                RouteModalType.Popup,
                _ =>
                {
                    // A misbehaving decider that pops instead of answering. Nothing throws, and the answer it
                    // then gives is moot because the route has already gone.
                    route.Pop();
                    return Task.FromResult(PopDecision.Allow());
                }
            );
            host.Navigator.Push(route);
            host.Settle();

            var outcome = host.Navigator.RequestPop(route, "why");
            host.Settle();

            Assert.AreEqual(PopOutcome.NotTopmost, outcome.Result);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
            Assert.IsNull(route.PopTask.Result.Request);
        }

        [Test]
        public void RequestReplace_WhenTheOutgoingRouteAllows_SwapsInOneStackChange()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();
            host.End();

            var outgoing = new DecidingRoute("D", RouteModalType.Popup, Allow);
            host.Begin("push D");
            host.Navigator.Push(outgoing);
            host.Settle();
            host.End();

            var incoming = host.Create("E", RouteModalType.Popup, RouteFlavour.Plain);
            host.Begin("request replace D with E");
            var outcome = host.Navigator.RequestReplace(outgoing, incoming, "switch");
            host.Settle();
            host.End();

            Assert.AreEqual(PopOutcome.Popped, outcome.Result);
            Assert.AreEqual("switch", outgoing.PopTask.Result.Request);

            host.AssertTrace(
                "mount A",
                "  > WillPush(A, null)",
                "  A OnInitialize",
                "  > DidPush(A, null)",
                "  A OnCreate",
                "  A OnResume",
                "  A OnFocus",
                "  stack: [A]",
                "push D",
                "  > WillPush(D, A)",
                "  A OnFocusLost",
                "  > DidPush(D, A)",
                "  stack: [D, A]",
                "request replace D with E",
                "  > WillReplace(E, D)",
                "  E OnInitialize",
                "  > DidReplace(E, D)",
                "  E OnCreate",
                "  E OnResume",
                "  E OnFocus",
                "  stack: [E, A]"
            );
        }

        [Test]
        public void RequestReplace_WhenTheOutgoingRouteRefuses_PushesNothing()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var outgoing = new DecidingRoute("D", RouteModalType.Popup, Refuse);
            host.Navigator.Push(outgoing);
            host.Settle();

            var incoming = host.Create("E", RouteModalType.Popup, RouteFlavour.Plain);
            var outcome = host.Navigator.RequestReplace(outgoing, incoming, "switch");
            host.Settle();

            Assert.AreEqual(PopOutcome.Refused, outcome.Result);
            Assert.AreSame(outgoing, host.Navigator.TopmostRoute);
            CollectionAssert.DoesNotContain(host.Navigator.NavigationStack, incoming);
            Assert.AreEqual(
                PopOutcome.NotTopmost,
                incoming.Pop().Result,
                "a route that was never placed is on top of nothing, so it was never attached"
            );
        }

        [Test]
        public void RequestReplace_WhenTheOutgoingRouteLeavesWhileDeciding_PushesNothing()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var decision = new TaskCompletionSource<PopDecision>();
            var outgoing = new DecidingRoute("D", RouteModalType.Popup, _ => decision.Task);
            host.Navigator.Push(outgoing);
            host.Settle();

            var incoming = host.Create("E", RouteModalType.Popup, RouteFlavour.Plain);
            var outcome = host.Navigator.RequestReplace(outgoing, incoming, "switch");
            host.Settle();

            outgoing.Pop();
            host.Settle();

            decision.SetResult(PopDecision.Allow());
            host.Settle();

            Assert.AreEqual(
                PopOutcome.NotTopmost,
                outcome.Result,
                "the swap it agreed to no longer exists"
            );
            CollectionAssert.DoesNotContain(host.Navigator.NavigationStack, incoming);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }

        /// <summary>
        ///     Same as for RequestPop: a replace arriving while the route decides behind its own dialog
        ///     joins the pending question rather than being turned away for not being on top.
        /// </summary>
        [Test]
        public void RequestReplace_WhileTheRouteDecidesBehindItsOwnDialog_JoinsThePendingRequest()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            Route confirmation = null;
            var route = new DecidingRoute(
                "D",
                RouteModalType.Popup,
                async _ =>
                {
                    confirmation = host.Create("Confirm", RouteModalType.Popup, RouteFlavour.Plain);
                    host.Navigator.Push(confirmation);
                    await confirmation.PopTask;
                    return PopDecision.Allow();
                }
            );
            host.Navigator.Push(route);
            host.Settle();

            var first = host.Navigator.RequestPop(route, "first");
            host.Settle();

            Assert.AreSame(
                confirmation,
                host.Navigator.TopmostRoute,
                "D is covered by the dialog it pushed to decide"
            );

            var incoming = host.Create("E", RouteModalType.Popup, RouteFlavour.Plain);
            var replace = host.Navigator.RequestReplace(route, incoming, "switch");

            Assert.IsFalse(replace.IsCompleted, "joined, not turned away with NotTopmost");
            Assert.AreEqual(1, route.TimesAsked);

            confirmation.Pop();
            host.Settle();

            Assert.AreEqual(PopOutcome.Popped, first.Result);
            Assert.AreEqual(
                PopOutcome.NotTopmost,
                replace.Result,
                "the route left through the first request; this replace did not happen"
            );
            CollectionAssert.DoesNotContain(host.Navigator.NavigationStack, incoming);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }

        /// <summary>
        ///     A requester whose command fails is told so, and the route is released for the next
        ///     request. Left pending, the failed request would hold the route's slot for good and every
        ///     later request for it would join a task that never completes.
        /// </summary>
        [Test]
        public void RequestReplace_WhenTheIncomingRouteFailsToInitialize_FailsTheRequester_AndReleasesTheRoute()
        {
            // The failing initialization reaches ProcessCommandsLoop, which logs it; that is the point of
            // the fixture.

            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var outgoing = new DecidingRoute("D", RouteModalType.Popup, Allow);
            host.Navigator.Push(outgoing);
            host.Settle();

            var broken = new FailingToInitializeRoute("X");
            var outcome = host.Navigator.RequestReplace(outgoing, broken, "switch");
            host.Settle();

            Assert.IsTrue(
                outcome.IsCompleted,
                "the requester is answered rather than left waiting on a replace that will never finish"
            );
            Assert.IsTrue(outcome.IsFaulted, "and answered with the failure");
            Assert.AreSame(
                outgoing,
                host.Navigator.TopmostRoute,
                "the outgoing route was never touched: the incoming one failed before it"
            );
            CollectionAssert.DoesNotContain(host.Navigator.NavigationStack, broken);

            var again = host.Navigator.RequestPop(outgoing, "again");
            host.Settle();

            Assert.AreEqual(
                2,
                outgoing.TimesAsked,
                "the failed request released the route, so it is asked afresh"
            );
            Assert.IsTrue(again.IsCompleted, "and the new request is answered on its own terms");
            Assert.AreEqual(PopOutcome.Popped, again.Result);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
            Assert.That(Zone.Faults, Is.Not.Empty, "the failure is reported rather than swallowed");
        }

        /// <summary>
        ///     The other way a replace can fail: the outgoing route's destroy throws. Replace deliberately
        ///     does not commit its removal against that, so the route stays; the requester must still be
        ///     answered and the route released.
        /// </summary>
        [Test]
        public void RequestReplace_WhenTheOutgoingRouteFailsToDestroy_FailsTheRequester_AndReleasesTheRoute()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var outgoing = host.Create("B", RouteModalType.Popup, RouteFlavour.ThrowsOnDestroy);
            host.Navigator.Push(outgoing);
            host.Settle();

            var incoming = host.Create("E", RouteModalType.Popup, RouteFlavour.Plain);
            var outcome = host.Navigator.RequestReplace(outgoing, incoming, "switch");
            host.Settle();

            Assert.IsTrue(
                outcome.IsCompleted,
                "the requester is answered rather than left waiting"
            );
            Assert.IsTrue(outcome.IsFaulted, "and answered with the failure");
            CollectionAssert.DoesNotContain(
                host.Navigator.NavigationStack,
                incoming,
                "the swap was abandoned before the incoming route was placed"
            );

            var retry = host.Create("F", RouteModalType.Popup, RouteFlavour.Plain);
            var again = host.Navigator.RequestReplace(outgoing, retry, "again");
            host.Settle();

            Assert.AreNotSame(
                outcome,
                again,
                "the failed request no longer holds the route's slot"
            );
            Assert.IsTrue(again.IsCompleted, "a new request is answered on its own terms");
            Assert.That(Zone.Faults, Is.Not.Empty, "the failure is reported rather than swallowed");
        }

        [Test]
        public void RequestPopTo_StopsAtTheFirstRouteThatRefuses_AndSaysWhich()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var root = host.Navigator.TopmostRoute;
            var stubborn = new DecidingRoute("B", RouteModalType.Fullscreen, Refuse);
            host.Navigator.Push(stubborn);
            host.Settle();

            var willing = new DecidingRoute("C", RouteModalType.Fullscreen, Allow);
            host.Navigator.Push(willing);
            host.Settle();

            var outcome = host.Navigator.RequestPopTo(root, "home");
            host.Settle();

            Assert.IsFalse(outcome.Result.Reached);
            Assert.AreSame(stubborn, outcome.Result.StoppedAt);
            Assert.AreEqual(PopOutcome.Refused, outcome.Result.Outcome);
            Assert.AreEqual(1, willing.TimesAsked, "C was asked, agreed, and went");
            Assert.AreEqual(1, stubborn.TimesAsked);
            Assert.AreSame(stubborn, host.Navigator.TopmostRoute);
            Assert.AreEqual("home", willing.PopTask.Result.Request);
        }

        [Test]
        public void RequestPopTo_WhenEveryRouteAgrees_ReachesTheTarget()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var root = host.Navigator.TopmostRoute;
            host.Navigator.Push(new DecidingRoute("B", RouteModalType.Fullscreen, Allow));
            host.Settle();
            host.Navigator.Push(new DecidingRoute("C", RouteModalType.Fullscreen, Allow));
            host.Settle();

            var outcome = host.Navigator.RequestPopTo(root, "home");
            host.Settle();

            Assert.IsTrue(outcome.Result.Reached);
            Assert.AreSame(root, host.Navigator.TopmostRoute);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }

        [Test]
        public void Replace_UnAsked_TearsTheRouteDownWithoutConsultingIt()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var route = new DecidingRoute("D", RouteModalType.Popup, Refuse);
            host.Navigator.Push(route);
            host.Settle();

            host.Navigator.Replace(host.Create("E", RouteModalType.Popup, RouteFlavour.Plain));
            host.Settle();

            Assert.AreEqual(
                0,
                route.TimesAsked,
                "an un-asked replace does not consult the route, even one that would refuse"
            );
            Assert.IsTrue(route.PopTask.IsCompleted);
            Assert.AreEqual(PopCause.Teardown, route.PopTask.Result.Cause);
            Assert.IsFalse(route.PopTask.Result.HasValue);
        }

        [Test]
        public void NewRoot_UnAsked_TearsEveryRouteDownWithoutConsultingIt()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var root = host.Navigator.TopmostRoute;
            var route = new DecidingRoute("D", RouteModalType.Fullscreen, Refuse);
            host.Navigator.Push(route);
            host.Settle();

            host.Navigator.NewRoot(host.Create("E", RouteModalType.Fullscreen, RouteFlavour.Plain));
            host.Settle();

            Assert.AreEqual(0, route.TimesAsked);
            Assert.AreEqual(PopCause.Teardown, route.PopTask.Result.Cause);
            Assert.AreEqual(PopCause.Teardown, root.PopTask.Result.Cause);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }

        [Test]
        public void Unmount_TearsEveryRouteDownWithTheTeardownMarker()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var route = new DecidingRoute("D", RouteModalType.Popup, Refuse);
            host.Navigator.Push(route);
            host.Settle();

            host.Unmount();
            host.PumpFrames(3);

            Assert.IsTrue(route.PopTask.IsCompleted);
            Assert.AreEqual(PopCause.Teardown, route.PopTask.Result.Cause);
        }

        [Test]
        public void Pop_OnARouteThatWasNeverPushed_ReportsNotTopmost()
        {
            // On top of nothing, so nothing to do. Not an exception: owners pop in teardown paths, and a
            // route handed to a fake or stubbed navigation service is an ordinary thing in a test.
            var route = new DecidingRoute("Loose", RouteModalType.Popup, Allow);

            Assert.AreEqual(PopOutcome.NotTopmost, route.Pop().Result);
        }

        [Test]
        public void Pop_IssuedBeforeItsPushHasRun_QueuesBehindThePush()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            // An animated route mid-exit keeps the command loop busy, so the next push waits.
            var animated = host.Create("B", RouteModalType.Fullscreen, RouteFlavour.AnimatedPage);
            host.Navigator.Push(animated);
            host.Settle();
            animated.Pop();

            var route = new DecidingRoute("C", RouteModalType.Popup, Allow);
            host.Navigator.Push(route);

            // The owner withdraws it before the push has even run: no throw, and the pop lands after.
            var outcome = route.Pop();
            host.Settle();

            Assert.AreEqual(
                PopOutcome.Popped,
                outcome.Result,
                "only possible if the route was attached to its navigator when the push was issued"
            );
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }

        [Test]
        public void Pop_OnARouteThatAlreadyLeft_ReportsNotTopmostRatherThanThrowing()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var route = new DecidingRoute("D", RouteModalType.Popup, Allow);
            host.Navigator.Push(route);
            host.Settle();

            route.Pop();
            host.Settle();

            var again = route.Pop();
            host.Settle();

            Assert.AreEqual(
                PopOutcome.NotTopmost,
                again.Result,
                "an owner tidying up after its route already left must be able to do so harmlessly"
            );
        }

        /// <summary>
        ///     A route's own chrome -- a barrier tap, its own close button -- closes it by asking, so that
        ///     the route's decision still runs. Same protocol as a request from outside, just issued from
        ///     within.
        /// </summary>
        [Test]
        public void RequestPop_FromTheRouteItself_AsksItAndPopsItCarryingTheRequest()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var route = new DecidingRoute("D", RouteModalType.Popup, Allow);
            host.Navigator.Push(route);
            host.Settle();

            var request = new object();
            var outcome = route.AskToLeave(request);
            host.Settle();

            Assert.AreEqual(PopOutcome.Popped, outcome.Result);
            Assert.AreEqual(
                1,
                route.TimesAsked,
                "asking on its own behalf still consults the route"
            );
            Assert.AreSame(request, route.LastRequest);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
            Assert.AreSame(
                request,
                route.PopTask.Result.Request,
                "the result names the request, as for any asked pop"
            );
        }

        [Test]
        public void RequestPop_FromTheRouteItself_WhenItRefuses_LeavesItInPlace()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var route = new DecidingRoute("D", RouteModalType.Popup, Refuse);
            host.Navigator.Push(route);
            host.Settle();

            var outcome = route.AskToLeave("why");
            host.Settle();

            Assert.AreEqual(
                PopOutcome.Refused,
                outcome.Result,
                "the route's own chrome gets no more authority than anyone else who asks"
            );
            Assert.AreSame(route, host.Navigator.TopmostRoute);
            Assert.IsFalse(route.PopTask.IsCompleted);
        }

        [Test]
        public void RequestPop_FromARouteThatWasNeverPushed_ReportsNotTopmostWithoutAskingIt()
        {
            var route = new DecidingRoute("Loose", RouteModalType.Popup, Allow);

            Assert.AreEqual(PopOutcome.NotTopmost, route.AskToLeave("why").Result);
            Assert.AreEqual(
                0,
                route.TimesAsked,
                "on top of nothing, so there is nothing to decide"
            );

            // The null-request rule holds regardless of whether the route has a navigator to forward to.
            Assert.Throws<ArgumentNullException>(() => route.AskToLeave(null));
        }

        /// <summary>
        ///     A null <see cref="PopResult.Request"/> is how a result says the route closed itself, so no
        ///     request may be null: it would make an asked pop indistinguishable from a self-close.
        /// </summary>
        [Test]
        public void Request_WithANullRequest_IsRejectedBeforeTheRouteIsAsked()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var route = new DecidingRoute("D", RouteModalType.Popup, Allow);
            host.Navigator.Push(route);
            host.Settle();

            var incoming = host.Create("E", RouteModalType.Popup, RouteFlavour.Plain);

            Assert.Throws<ArgumentNullException>(() => host.Navigator.RequestPop(route, null));
            Assert.Throws<ArgumentNullException>(() =>
                host.Navigator.RequestReplace(route, incoming, null)
            );
            Assert.Throws<ArgumentNullException>(() => host.Navigator.RequestPopTo(null, null));

            Assert.AreEqual(
                0,
                route.TimesAsked,
                "rejected at the call, before the route is consulted"
            );
            Assert.AreEqual(2, host.Navigator.NavigationStack.Count);
        }

        /// <summary>
        ///     Nobody awaits a back press, and the decision runs outside the command loop, so a hook that
        ///     throws on back has no caller and no loop to report to. It must still be reported.
        /// </summary>
        [Test]
        public void Back_WhenTheRouteThrowsWhileDeciding_IsReportedRatherThanSwallowed()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var timesAsked = 0;
            var route = new DecidingRoute(
                "D",
                RouteModalType.Popup,
                request =>
                {
                    if (timesAsked++ == 0)
                    {
                        throw new InvalidOperationException("decision failed on " + request);
                    }

                    return Allow(request);
                }
            );
            route.WithPopOnBack(host.Navigator, "back");
            host.Navigator.Push(route);
            host.Settle();

            Assert.IsTrue(
                host.Navigator.HandleBack(),
                "back was handled, whatever the route then did with it"
            );
            host.Settle();

            // Asserted on the fault rather than on Unity's console: the clock installs a reporter, so
            // the exception reaches that instead of the log, and this says which phase it escaped.
            Assert.That(Zone.Faults, Has.Count.EqualTo(1));
            Assert.That(Zone.Faults[0].Phase, Is.EqualTo("BackAction"));
            Assert.That(Zone.Faults[0].Exception.Message, Does.Contain("decision failed on back"));

            Assert.AreSame(
                route,
                host.Navigator.TopmostRoute,
                "a decision that failed is not a decision to leave"
            );

            // The failed question released the route: the next back press asks it again.
            Assert.IsTrue(host.Navigator.HandleBack());
            host.Settle();

            Assert.AreEqual(2, timesAsked);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }
    }
}
