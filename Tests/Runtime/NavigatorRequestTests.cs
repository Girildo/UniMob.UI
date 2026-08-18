using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UniMob.UI.Widgets;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     A route that answers <see cref="Route.OnPopRequested"/> with whatever the fixture supplies, and
    ///     counts how often it was asked.
    /// </summary>
    internal sealed class DecidingRoute : Route
    {
        private readonly Func<object, Task<PopDecision>> _decide;

        public DecidingRoute(string key, RouteModalType modalType, Func<object, Task<PopDecision>> decide)
            : base(new RouteSettings(key, modalType))
        {
            _decide = decide;
        }

        public int TimesAsked { get; private set; }

        public object LastRequest { get; private set; }

        public override Widget Build(BuildContext context) => new Empty();

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
    public class NavigatorRequestTests
    {
        private static Task<PopDecision> Allow(object _) => Task.FromResult(PopDecision.Allow());

        private static Task<PopDecision> Refuse(object _) => Task.FromResult(PopDecision.Refuse());

        [UnityTest]
        public IEnumerator RequestPop_WhenTheRouteAllows_PopsItCarryingTheRequest()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var route = new DecidingRoute("D", RouteModalType.Popup, Allow);
            host.Navigator.Push(route);
            yield return host.Settle();

            var request = new object();
            var outcome = host.Navigator.RequestPop(route, request);
            yield return host.Settle();

            Assert.AreEqual(PopOutcome.Popped, outcome.Result);
            Assert.AreEqual(1, route.TimesAsked);
            Assert.AreSame(request, route.LastRequest, "the route is handed the request as it was given");
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);

            Assert.IsTrue(route.PopTask.IsCompleted);
            Assert.IsFalse(route.PopTask.Result.HasValue, "an untyped route cannot attach a value to its answer");
            Assert.AreSame(request, route.PopTask.Result.Request, "the result names the request that popped it");
        }

        [UnityTest]
        public IEnumerator RequestPop_WhenTheRouteRefuses_LeavesItInPlace()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var route = new DecidingRoute("D", RouteModalType.Popup, Refuse);
            host.Navigator.Push(route);
            yield return host.Settle();

            var outcome = host.Navigator.RequestPop(route, "why");
            yield return host.Settle();

            Assert.AreEqual(PopOutcome.Refused, outcome.Result);
            Assert.AreSame(route, host.Navigator.TopmostRoute);
            Assert.IsFalse(route.PopTask.IsCompleted, "a refused route has not left, so nothing awaiting it resumes");
        }

        [UnityTest]
        public IEnumerator RequestPop_ForARouteThatIsNotOnTop_DoesNothingAndSaysSo()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var root = host.Navigator.TopmostRoute;
            var top = new DecidingRoute("D", RouteModalType.Popup, Allow);
            host.Navigator.Push(top);
            yield return host.Settle();

            var outcome = host.Navigator.RequestPop(root, "why");
            yield return host.Settle();

            Assert.AreEqual(PopOutcome.NotTopmost, outcome.Result);
            Assert.AreEqual(2, host.Navigator.NavigationStack.Count, "nothing moved");
            Assert.AreSame(top, host.Navigator.TopmostRoute);
        }

        [UnityTest]
        public IEnumerator RequestPop_ForTheLastRoute_ReportsLastRoute()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var root = host.Navigator.TopmostRoute;
            var outcome = host.Navigator.RequestPop(root, "why");
            yield return host.Settle();

            Assert.AreEqual(PopOutcome.LastRoute, outcome.Result);
            Assert.AreSame(root, host.Navigator.TopmostRoute);
        }

        [UnityTest]
        public IEnumerator RequestPop_WhileTheRouteIsStillDeciding_IsAskedOnceAndSharesTheOutcome()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var decision = new TaskCompletionSource<PopDecision>();
            var route = new DecidingRoute("D", RouteModalType.Popup, _ => decision.Task);
            host.Navigator.Push(route);
            yield return host.Settle();

            var first = host.Navigator.RequestPop(route, "first");
            var second = host.Navigator.RequestPop(route, "second");
            yield return host.Settle();

            Assert.IsFalse(first.IsCompleted, "the route has not answered yet");
            Assert.AreEqual(1, route.TimesAsked, "the second requester joins the pending question");
            Assert.AreSame(first, second, "one pending answer, shared");

            decision.SetResult(PopDecision.Allow());
            yield return host.Settle();

            Assert.AreEqual(PopOutcome.Popped, first.Result);
            Assert.AreEqual(PopOutcome.Popped, second.Result);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
            Assert.AreEqual("first", route.PopTask.Result.Request, "the pop carries the request that started the question");
        }

        [UnityTest]
        public IEnumerator RequestPop_WhenTheDecisionNavigates_DoesNotDeadlock()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            // The route answers by pushing a "confirmation" and waiting for it, which is only possible
            // because the decision runs outside the command loop.
            Route confirmation = null;
            var route = new DecidingRoute("D", RouteModalType.Popup, async _ =>
            {
                confirmation = host.Create("Confirm", RouteModalType.Popup, RouteFlavour.Plain);
                host.Navigator.Push(confirmation);
                await confirmation.PopTask;
                return PopDecision.Allow();
            });
            host.Navigator.Push(route);
            yield return host.Settle();

            var outcome = host.Navigator.RequestPop(route, "why");
            yield return host.Settle();

            Assert.IsFalse(outcome.IsCompleted);
            Assert.AreSame(confirmation, host.Navigator.TopmostRoute, "the confirmation is up while D decides");

            confirmation.Pop();
            yield return host.Settle();

            Assert.AreEqual(PopOutcome.Popped, outcome.Result);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }

        [UnityTest]
        public IEnumerator RequestPop_WhenTheRoutePopsItselfWhileDeciding_ReportsNotTopmost()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var decision = new TaskCompletionSource<PopDecision>();
            var route = new DecidingRoute("D", RouteModalType.Popup, _ => decision.Task);
            host.Navigator.Push(route);
            yield return host.Settle();

            var outcome = host.Navigator.RequestPop(route, "why");
            yield return host.Settle();

            // Self is authoritative: the route's own pop lands now, whatever question is pending.
            var own = route.Pop();
            yield return host.Settle();

            Assert.AreEqual(PopOutcome.Popped, own.Result);
            Assert.IsNull(route.PopTask.Result.Request, "the route closed itself");

            decision.SetResult(PopDecision.Allow());
            yield return host.Settle();

            Assert.AreEqual(PopOutcome.NotTopmost, outcome.Result, "the request finds its route already gone");
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }

        [UnityTest]
        public IEnumerator RequestPop_WhenTheDecisionPopsTheRouteItself_QueuesRatherThanReenters()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            DecidingRoute route = null;
            route = new DecidingRoute("D", RouteModalType.Popup, _ =>
            {
                // A misbehaving decider that pops instead of answering. Nothing throws, and the answer it
                // then gives is moot because the route has already gone.
                route.Pop();
                return Task.FromResult(PopDecision.Allow());
            });
            host.Navigator.Push(route);
            yield return host.Settle();

            var outcome = host.Navigator.RequestPop(route, "why");
            yield return host.Settle();

            Assert.AreEqual(PopOutcome.NotTopmost, outcome.Result);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
            Assert.IsNull(route.PopTask.Result.Request);
        }

        [UnityTest]
        public IEnumerator RequestReplace_WhenTheOutgoingRouteAllows_SwapsInOneStackChange()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();
            host.End();

            var outgoing = new DecidingRoute("D", RouteModalType.Popup, Allow);
            host.Begin("push D");
            host.Navigator.Push(outgoing);
            yield return host.Settle();
            host.End();

            var incoming = host.Create("E", RouteModalType.Popup, RouteFlavour.Plain);
            host.Begin("request replace D with E");
            var outcome = host.Navigator.RequestReplace(outgoing, incoming, "switch");
            yield return host.Settle();
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
                "  stack: [E, A]");
        }

        [UnityTest]
        public IEnumerator RequestReplace_WhenTheOutgoingRouteRefuses_PushesNothing()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var outgoing = new DecidingRoute("D", RouteModalType.Popup, Refuse);
            host.Navigator.Push(outgoing);
            yield return host.Settle();

            var incoming = host.Create("E", RouteModalType.Popup, RouteFlavour.Plain);
            var outcome = host.Navigator.RequestReplace(outgoing, incoming, "switch");
            yield return host.Settle();

            Assert.AreEqual(PopOutcome.Refused, outcome.Result);
            Assert.AreSame(outgoing, host.Navigator.TopmostRoute);
            CollectionAssert.DoesNotContain(host.Navigator.NavigationStack, incoming);
            Assert.IsNull(incoming.Navigator, "a route that was never placed knows no navigator");
        }

        [UnityTest]
        public IEnumerator RequestReplace_WhenTheOutgoingRouteLeavesWhileDeciding_PushesNothing()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var decision = new TaskCompletionSource<PopDecision>();
            var outgoing = new DecidingRoute("D", RouteModalType.Popup, _ => decision.Task);
            host.Navigator.Push(outgoing);
            yield return host.Settle();

            var incoming = host.Create("E", RouteModalType.Popup, RouteFlavour.Plain);
            var outcome = host.Navigator.RequestReplace(outgoing, incoming, "switch");
            yield return host.Settle();

            outgoing.Pop();
            yield return host.Settle();

            decision.SetResult(PopDecision.Allow());
            yield return host.Settle();

            Assert.AreEqual(PopOutcome.NotTopmost, outcome.Result, "the swap it agreed to no longer exists");
            CollectionAssert.DoesNotContain(host.Navigator.NavigationStack, incoming);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }

        [UnityTest]
        public IEnumerator RequestPopTo_StopsAtTheFirstRouteThatRefuses_AndSaysWhich()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var root = host.Navigator.TopmostRoute;
            var stubborn = new DecidingRoute("B", RouteModalType.Fullscreen, Refuse);
            host.Navigator.Push(stubborn);
            yield return host.Settle();

            var willing = new DecidingRoute("C", RouteModalType.Fullscreen, Allow);
            host.Navigator.Push(willing);
            yield return host.Settle();

            var outcome = host.Navigator.RequestPopTo(root, "home");
            yield return host.Settle();

            Assert.IsFalse(outcome.Result.Reached);
            Assert.AreSame(stubborn, outcome.Result.StoppedAt);
            Assert.AreEqual(PopOutcome.Refused, outcome.Result.Outcome);
            Assert.AreEqual(1, willing.TimesAsked, "C was asked, agreed, and went");
            Assert.AreEqual(1, stubborn.TimesAsked);
            Assert.AreSame(stubborn, host.Navigator.TopmostRoute);
            Assert.AreEqual("home", willing.PopTask.Result.Request);
        }

        [UnityTest]
        public IEnumerator RequestPopTo_WhenEveryRouteAgrees_ReachesTheTarget()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var root = host.Navigator.TopmostRoute;
            host.Navigator.Push(new DecidingRoute("B", RouteModalType.Fullscreen, Allow));
            yield return host.Settle();
            host.Navigator.Push(new DecidingRoute("C", RouteModalType.Fullscreen, Allow));
            yield return host.Settle();

            var outcome = host.Navigator.RequestPopTo(root, "home");
            yield return host.Settle();

            Assert.IsTrue(outcome.Result.Reached);
            Assert.AreSame(root, host.Navigator.TopmostRoute);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }

        [UnityTest]
        public IEnumerator Replace_UnAsked_TearsTheRouteDownWithoutConsultingIt()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var route = new DecidingRoute("D", RouteModalType.Popup, Refuse);
            host.Navigator.Push(route);
            yield return host.Settle();

            host.Navigator.Replace(host.Create("E", RouteModalType.Popup, RouteFlavour.Plain));
            yield return host.Settle();

            Assert.AreEqual(0, route.TimesAsked, "an un-asked replace does not consult the route, even one that would refuse");
            Assert.IsTrue(route.PopTask.IsCompleted);
            Assert.AreSame(PopRequest.Teardown, route.PopTask.Result.Request);
            Assert.IsFalse(route.PopTask.Result.HasValue);
        }

        [UnityTest]
        public IEnumerator NewRoot_UnAsked_TearsEveryRouteDownWithoutConsultingIt()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var root = host.Navigator.TopmostRoute;
            var route = new DecidingRoute("D", RouteModalType.Fullscreen, Refuse);
            host.Navigator.Push(route);
            yield return host.Settle();

            host.Navigator.NewRoot(host.Create("E", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();

            Assert.AreEqual(0, route.TimesAsked);
            Assert.AreSame(PopRequest.Teardown, route.PopTask.Result.Request);
            Assert.AreSame(PopRequest.Teardown, root.PopTask.Result.Request);
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
        }

        [UnityTest]
        public IEnumerator Unmount_TearsEveryRouteDownWithTheTeardownMarker()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var route = new DecidingRoute("D", RouteModalType.Popup, Refuse);
            host.Navigator.Push(route);
            yield return host.Settle();

            host.Unmount();
            yield return host.PumpFrames(3);

            Assert.IsTrue(route.PopTask.IsCompleted);
            Assert.AreSame(PopRequest.Teardown, route.PopTask.Result.Request);
        }

        [UnityTest]
        public IEnumerator Pop_OnARouteThatWasNeverPushed_Throws()
        {
            var route = new DecidingRoute("Loose", RouteModalType.Popup, Allow);

            Assert.Throws<InvalidOperationException>(() => route.Pop());
            yield break;
        }

        [UnityTest]
        public IEnumerator Pop_OnARouteThatAlreadyLeft_ReportsNotTopmostRatherThanThrowing()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var route = new DecidingRoute("D", RouteModalType.Popup, Allow);
            host.Navigator.Push(route);
            yield return host.Settle();

            route.Pop();
            yield return host.Settle();

            var again = route.Pop();
            yield return host.Settle();

            Assert.AreEqual(PopOutcome.NotTopmost, again.Result,
                "an owner tidying up after its route already left must be able to do so harmlessly");
        }
    }
}
