using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UniMob.UI.Widgets;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     A typed route whose answer to a request the fixture supplies.
    /// </summary>
    internal sealed class DecidingRoute<T> : Route<T>
    {
        private readonly Func<object, Task<PopDecision<T>>> _decide;

        public DecidingRoute(string key, RouteModalType modalType, Func<object, Task<PopDecision<T>>> decide)
            : base(new RouteSettings(key, modalType))
        {
            _decide = decide;
        }

        public override Widget Build(BuildContext context) => new Empty();

        protected override Task<PopDecision<T>> DecidePop(object request) => _decide(request);
    }

    /// <summary>
    ///     Pins that awaiting a <see cref="Route{T}"/> yields a <see cref="PopResult{T}"/>: the value is a
    ///     T by construction, whether the route popped itself or answered a request, and every other way
    ///     of leaving reports "no value" plus who caused it.
    /// </summary>
    public class RouteResultTypingTests
    {
        private static Task<PopDecision<int>> AllowWith(int value) => Task.FromResult(PopDecision<int>.Allow(value));

        [UnityTest]
        public IEnumerator PopWithValue_CompletesTheTypedResult_AndTheUntypedOne()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var route = new DecidingRoute<int>("D", RouteModalType.Popup, _ => AllowWith(0));
            host.Navigator.Push(route);
            yield return host.Settle();

            var outcome = route.Pop(42);
            yield return host.Settle();

            Assert.AreEqual(PopOutcome.Popped, outcome.Result);

            var (hasValue, value) = route.Result.Result;
            Assert.IsTrue(hasValue);
            Assert.AreEqual(42, value);
            Assert.AreEqual(PopCause.Self, route.Result.Result.Cause, "the route closed itself");
            Assert.IsNull(route.Result.Result.Request);

            Assert.IsTrue(route.PopTask.Result.HasValue);
            Assert.AreEqual(42, route.PopTask.Result.Value, "the untyped task carries the same value, boxed");
        }

        [UnityTest]
        public IEnumerator PopWithoutValue_ReportsNoValue()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var route = new DecidingRoute<int>("D", RouteModalType.Popup, _ => AllowWith(0));
            host.Navigator.Push(route);
            yield return host.Settle();

            route.Pop();
            yield return host.Settle();

            Assert.IsFalse(route.Result.Result.HasValue,
                "HasValue rather than a null check, because default(int) is a legitimate value");
            Assert.AreEqual(default(int), route.Result.Result.Value);
            Assert.IsNull(route.Result.Result.Request);
        }

        [UnityTest]
        public IEnumerator RequestPop_WhenTheDecisionSuppliesAValue_TheResultCarriesItAndTheRequest()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var route = new DecidingRoute<int>("D", RouteModalType.Popup, _ => AllowWith(7));
            host.Navigator.Push(route);
            yield return host.Settle();

            var request = new object();
            var outcome = host.Navigator.RequestPop(route, request);
            yield return host.Settle();

            Assert.AreEqual(PopOutcome.Popped, outcome.Result);
            Assert.IsTrue(route.Result.Result.HasValue);
            Assert.AreEqual(7, route.Result.Result.Value);
            Assert.AreEqual(PopCause.Requested, route.Result.Result.Cause);
            Assert.AreSame(request, route.Result.Result.Request,
                "who asked and what the route answered travel together");
        }

        [UnityTest]
        public IEnumerator RequestPop_WhenTheDecisionAllowsWithoutAValue_TheResultHasNone()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var route = new DecidingRoute<int>("D", RouteModalType.Popup,
                _ => Task.FromResult(PopDecision<int>.Allow()));
            host.Navigator.Push(route);
            yield return host.Settle();

            host.Navigator.RequestPop(route, "why");
            yield return host.Settle();

            Assert.IsFalse(route.Result.Result.HasValue);
            Assert.AreEqual("why", route.Result.Result.Request);
        }

        [UnityTest]
        public IEnumerator RequestPop_WhenTheDecisionRefuses_TheResultStaysPending()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var route = new DecidingRoute<int>("D", RouteModalType.Popup,
                _ => Task.FromResult(PopDecision<int>.Refuse()));
            host.Navigator.Push(route);
            yield return host.Settle();

            var outcome = host.Navigator.RequestPop(route, "why");
            yield return host.Settle();

            Assert.AreEqual(PopOutcome.Refused, outcome.Result);
            Assert.IsFalse(route.Result.IsCompleted);
        }

        [UnityTest]
        public IEnumerator UnAskedReplace_CompletesTheTypedResultWithNoValueAndTheTeardownMarker()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var route = new DecidingRoute<int>("D", RouteModalType.Popup, _ => AllowWith(1));
            host.Navigator.Push(route);
            yield return host.Settle();

            host.Navigator.Replace(host.Create("E", RouteModalType.Popup, RouteFlavour.Plain));
            yield return host.Settle();

            Assert.IsTrue(route.Result.IsCompleted);
            Assert.IsFalse(route.Result.Result.HasValue);
            Assert.AreEqual(PopCause.Teardown, route.Result.Result.Cause);
            Assert.IsNull(route.Result.Result.Request, "nobody asked, so there is nothing to have asked with");
        }

        [UnityTest]
        public IEnumerator PopWithNullOnAReferenceTypedRoute_IsAValue()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var route = new DecidingRoute<string>("D", RouteModalType.Popup,
                _ => Task.FromResult(PopDecision<string>.Allow()));
            host.Navigator.Push(route);
            yield return host.Settle();

            route.Pop(null);
            yield return host.Settle();

            Assert.IsTrue(route.Result.Result.HasValue, "the route said it had a value; that the value is null is its business");
            Assert.IsNull(route.Result.Result.Value);
        }

        /// <summary>
        ///     The typed and the untyped result are two views of one close. Whichever a caller awaits,
        ///     the other must already be complete by the time the caller resumes.
        /// </summary>
        /// <remarks>
        ///     The two completers are set one after the other, and the continuations of the first used to
        ///     run inline inside its completion, so an awaiter of <c>PopTask</c> resumed while
        ///     <c>Result</c> was still pending. Both completers now defer their continuations, which is what
        ///     this pins from both sides.
        /// </remarks>
        [UnityTest]
        public IEnumerator AwaitingEitherResult_FindsTheOtherAlreadyComplete()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var route = new DecidingRoute<int>("D", RouteModalType.Popup, _ => AllowWith(0));
            host.Navigator.Push(route);
            yield return host.Settle();

            var typedWasCompleteWhenUntypedResumed = false;
            var untypedWasCompleteWhenTypedResumed = false;

            async Task AwaitUntyped()
            {
                await route.PopTask;
                typedWasCompleteWhenUntypedResumed = route.Result.IsCompleted;
            }

            async Task AwaitTyped()
            {
                await route.Result;
                untypedWasCompleteWhenTypedResumed = route.PopTask.IsCompleted;
            }

            var untypedAwaiter = AwaitUntyped();
            var typedAwaiter = AwaitTyped();

            route.Pop(42);
            yield return host.Settle();

            Assert.IsTrue(untypedAwaiter.IsCompleted, "the PopTask awaiter resumed");
            Assert.IsTrue(typedAwaiter.IsCompleted, "the Result awaiter resumed");
            Assert.IsTrue(typedWasCompleteWhenUntypedResumed,
                "an awaiter of PopTask must not resume before Result is complete");
            Assert.IsTrue(untypedWasCompleteWhenTypedResumed,
                "an awaiter of Result must not resume before PopTask is complete");
        }
    }
}
