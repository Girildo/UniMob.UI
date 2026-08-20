using System.Collections;
using NUnit.Framework;
using UniMob.UI.Navigation;
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
    ///         These use <see cref="RouteFlavour.ThrowsAsyncOnDestroy"/> because that is the shape of the
    ///         only failure the package produces on its own: PageRoute's exit-animation wait being
    ///         cancelled when disposal destroys the lifetime it is bound to. A handler that fails without
    ///         yielding must behave identically, which
    ///         <see cref="Destroy_FailingSynchronously_IsReportedLikeAnAsynchronousFailure"/> holds it to.
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

            var failing = host.Create(
                "B",
                RouteModalType.Fullscreen,
                RouteFlavour.ThrowsAsyncOnDestroy
            );
            host.Navigator.Push(failing);
            yield return host.Settle();

            host.Navigator.TopmostRoute.Pop();
            yield return host.Settle();

            CollectionAssert.DoesNotContain(
                host.Navigator.NavigationStack,
                failing,
                "a route whose destroy failed must still leave the stack"
            );
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
            Assert.AreEqual(
                ScreenState.Destroyed,
                failing.ScreenState,
                "the machine had already committed to Destroyed before the handler ran"
            );
        }

        [UnityTest]
        public IEnumerator Pop_WhenDestroyFailsAsynchronously_LeavesNoFinishedRouteOnTheStack()
        {
            LogAssert.ignoreFailingMessages = true;

            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            host.Navigator.Push(
                host.Create("B", RouteModalType.Fullscreen, RouteFlavour.ThrowsAsyncOnDestroy)
            );
            yield return host.Settle();

            host.Navigator.TopmostRoute.Pop();
            yield return host.Settle();

            AssertNoFinishedRouteOnTheStack(host);
        }

        /// <summary>
        ///     A walk down the stack asks and pops one route at a time, so one route failing to destroy is
        ///     that route's affair: it is removed all the same, and the walk carries on to the next.
        /// </summary>
        /// <remarks>
        ///     This used to stop at the failed route, when PopTo was one command removing several routes and
        ///     a throw aborted its remaining iterations. RequestPopTo issues one pop per route, each committed
        ///     on its own, and a committed pop reports Popped whatever its transition did on the way.
        /// </remarks>
        [UnityTest]
        public IEnumerator PopTo_WhenADestroyFailsAsynchronously_CarriesOnPastTheFailedRoute()
        {
            LogAssert.ignoreFailingMessages = true;

            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var root = host.Navigator.TopmostRoute;

            host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();

            var failing = host.Create(
                "C",
                RouteModalType.Fullscreen,
                RouteFlavour.ThrowsAsyncOnDestroy
            );
            host.Navigator.Push(failing);
            yield return host.Settle();

            host.Navigator.RequestPopTo(root, "test");
            yield return host.Settle();

            CollectionAssert.DoesNotContain(
                host.Navigator.NavigationStack,
                failing,
                "the route that failed to destroy is still removed"
            );
            Assert.AreEqual(
                1,
                host.Navigator.NavigationStack.Count,
                "the walk carries on past the failed route down to the root"
            );

            AssertNoFinishedRouteOnTheStack(host);
        }

        /// <summary>
        ///     Whether a destroy handler yields before failing must make no difference to what the
        ///     navigator sees.
        /// </summary>
        /// <remarks>
        ///     It used to make all the difference. ExecuteTransitionInternal guarded each await with
        ///     <c>!IsCompleted</c>, and a synchronously faulted task is completed, so a handler that
        ///     failed without yielding had its exception dropped: the transition reported success,
        ///     nothing was logged, and the route's PopTask was left pending forever. Since every handler
        ///     in the package completes synchronously, that was the default path rather than an edge
        ///     case. This fixture is the pair of
        ///     <see cref="Pop_WhenDestroyFailsAsynchronously_StillRemovesTheRoute"/>; the two must agree.
        /// </remarks>
        [UnityTest]
        public IEnumerator Destroy_FailingSynchronously_IsReportedLikeAnAsynchronousFailure()
        {
            LogAssert.ignoreFailingMessages = true;

            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var failing = host.Create("B", RouteModalType.Fullscreen, RouteFlavour.ThrowsOnDestroy);
            host.Navigator.Push(failing);
            yield return host.Settle();

            host.Navigator.TopmostRoute.Pop();
            yield return host.Settle();

            CollectionAssert.DoesNotContain(
                host.Navigator.NavigationStack,
                failing,
                "a route whose destroy failed must still leave the stack"
            );
            Assert.AreEqual(1, host.Navigator.NavigationStack.Count);
            AssertNoFinishedRouteOnTheStack(host);
        }

        private static void AssertNoFinishedRouteOnTheStack(NavigatorHost host)
        {
            foreach (var route in host.Navigator.NavigationStack)
            {
                Assert.AreNotEqual(
                    ScreenState.Destroyed,
                    route.ScreenState,
                    "no route on the stack may report itself destroyed: " + route.Key
                );
            }
        }
    }
}
