using System.Collections;
using NUnit.Framework;
using UniMob.UI.Widgets;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Covers the rule that a removal commits even when the transition that triggered it fails.
    /// </summary>
    /// <remarks>
    ///     TriggerStateMachine assigns the next state before running the handler, so a route being popped
    ///     reports Destroyed for the whole of its exit transition. If that transition fails and the pop is
    ///     skipped, the route stays on the stack announcing that it has finished, and TopmostRoute,
    ///     HandleBack and PopIfTopmost all keep answering with it.
    ///     <para>
    ///         Only an <i>asynchronous</i> failure can reach the navigator, which is why these use
    ///         <see cref="RouteFlavour.ThrowsAsyncOnDestroy"/>. See
    ///         <see cref="Destroy_FailingSynchronously_IsSwallowedByTheTransition"/> for the other half.
    ///         The real instance of an asynchronous failure is PageRoute's exit-animation wait being
    ///         cancelled when disposal destroys the lifetime it is bound to.
    ///     </para>
    ///     <para>
    ///         Only removals commit. Additions must still be able to fail without leaving anything behind,
    ///         which is also why ReplaceInternal is excluded: it removes before it adds, so committing its
    ///         removal against a failing destroy would end the command with an empty navigator. Replace is
    ///         made safe by ordering instead, not by committing.
    ///     </para>
    /// </remarks>
    public class NavigatorCommitTests
    {
        [UnityTest]
        public IEnumerator Pop_WhenDestroyFailsAsynchronously_StillRemovesTheRoute()
        {
            // The failing destroy reaches ProcessCommandsLoop, which logs it. That is the point of the
            // fixture, not an accident of it.
            LogAssert.ignoreFailingMessages = true;

            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var failing = host.Create("B", RouteModalType.Fullscreen, RouteFlavour.ThrowsAsyncOnDestroy);
            host.Navigator.Push(failing);
            yield return host.Settle();

            host.Navigator.Pop();
            yield return host.Settle();

            CollectionAssert.DoesNotContain(host.Navigator.NavigationStack, failing,
                "a route whose destroy failed must still leave the stack");
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
            Assert.AreEqual(ScreenState.Destroyed, failing.ScreenState,
                "the machine had already committed to Destroyed before the handler ran");
        }

        [UnityTest]
        public IEnumerator Pop_WhenDestroyFailsAsynchronously_LeavesNoFinishedRouteOnTheStack()
        {
            LogAssert.ignoreFailingMessages = true;

            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            host.Navigator.Push(
                host.Create("B", RouteModalType.Fullscreen, RouteFlavour.ThrowsAsyncOnDestroy));
            yield return host.Settle();

            host.Navigator.Pop();
            yield return host.Settle();

            AssertNoFinishedRouteOnTheStack(host);
        }

        /// <summary>
        ///     A failure part-way through a multi-route pop keeps everything already removed removed, and
        ///     stops at the route that failed rather than carrying on past it.
        /// </summary>
        [UnityTest]
        public IEnumerator PopTo_WhenADestroyFailsAsynchronously_StopsAtTheFailedRoute()
        {
            LogAssert.ignoreFailingMessages = true;

            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var root = host.Navigator.TopmostRoute;

            host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();

            var failing = host.Create("C", RouteModalType.Fullscreen, RouteFlavour.ThrowsAsyncOnDestroy);
            host.Navigator.Push(failing);
            yield return host.Settle();

            host.Navigator.PopTo(root);
            yield return host.Settle();

            CollectionAssert.DoesNotContain(host.Navigator.NavigationStack, failing,
                "the route that failed to destroy is still removed");
            Assert.AreEqual(2, host.Navigator.NavigationStack.Count,
                "the failure aborts the remaining iterations, so B survives");

            AssertNoFinishedRouteOnTheStack(host);
        }

        /// <summary>
        ///     A destroy handler that fails without yielding first fails silently, and the navigator
        ///     proceeds as though it succeeded.
        /// </summary>
        /// <remarks>
        ///     ExecuteTransitionInternal guards each await with <c>!IsCompleted</c>, and a synchronously
        ///     faulted task is completed, so the fault is never observed and never propagates. The route
        ///     is popped normally and nothing is logged, but its OnDestroy never reached the completer, so
        ///     PopTask is left pending forever -- a stranded awaiter with no diagnostic anywhere.
        ///     <para>
        ///         Pinned as current behaviour, not endorsed. Deliberately does not suppress log
        ///         assertions: the fixture passing is itself the evidence that nothing was reported.
        ///     </para>
        /// </remarks>
        [UnityTest]
        public IEnumerator Destroy_FailingSynchronously_IsSwallowedByTheTransition()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var failing = host.Create("B", RouteModalType.Fullscreen, RouteFlavour.ThrowsOnDestroy);
            host.Navigator.Push(failing);
            yield return host.Settle();

            host.Navigator.Pop();
            yield return host.Settle();

            Assert.AreEqual(1, host.Navigator.NavigationStack.Count,
                "the navigator saw no failure, so the pop completed as usual");
            Assert.IsFalse(failing.PopTask.IsCompleted,
                "OnDestroy threw before completing the pop, stranding every awaiter of PopTask");
        }

        private static void AssertNoFinishedRouteOnTheStack(NavigatorHost host)
        {
            foreach (var route in host.Navigator.NavigationStack)
            {
                Assert.AreNotEqual(ScreenState.Destroyed, route.ScreenState,
                    "no route on the stack may report itself destroyed: " + route.Key);
            }
        }
    }
}
