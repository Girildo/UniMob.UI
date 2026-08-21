using NUnit.Framework;
using UniMob.UI.Navigation;
using UniMob.UI.Widgets;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Covers the invariant that a route is never disposed without first having been ended.
    /// </summary>
    /// <remarks>
    ///     Pop, PopTo and Replace all destroy a route before its widget leaves the tree. Unmounting the
    ///     navigator used to be the exception: the widgets went away, <c>Route.Dispose</c> ran, and the
    ///     state machine was abandoned wherever it stood, so <c>OnDestroy</c> never ran and
    ///     <c>PopTask</c> never completed. Anything awaiting one waited forever.
    /// </remarks>
    public class RouteTeardownTests : NavigatorFixture
    {
        [Test]
        public void Unmount_CompletesPopTaskForEveryLiveRoute()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var root = host.Navigator.TopmostRoute;
            var pushed = host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain);

            host.Navigator.Push(pushed);
            host.Settle();

            Assert.IsFalse(root.PopTask.IsCompleted, "the root has not been popped");
            Assert.IsFalse(pushed.PopTask.IsCompleted, "the pushed route has not been popped");

            host.Unmount();
            host.PumpFrames(3);

            Assert.IsTrue(root.PopTask.IsCompleted, "unmount must answer the root's awaiters");
            Assert.IsTrue(
                pushed.PopTask.IsCompleted,
                "unmount must answer the pushed route's awaiters"
            );
        }

        /// <summary>
        ///     The route that most needs closing out: already at Destroyed and waiting on an exit
        ///     animation whose lifetime is about to be destroyed underneath it.
        /// </summary>
        /// <remarks>
        ///     Without a handler on the Destroyed self-transition this is the one case teardown would skip,
        ///     because the state machine is already where teardown wants to put it. Its pending
        ///     <c>Atom.When</c> is then cancelled by disposal and its <c>OnDestroy</c> never resumes, so
        ///     nothing else would ever complete the task.
        /// </remarks>
        [Test]
        public void Unmount_WhileAnExitAnimationIsRunning_StillCompletesPopTask()
        {
            // Deliberately does not suppress log assertions. Disposal cancels the pending Atom.When, and
            // that cancellation used to reach the console as a red exception on every unmount landing
            // mid-transition. This fixture passing is the evidence that it no longer does: Unity fails a
            // test on any unexpected logged exception.
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var animated = host.Create("B", RouteModalType.Fullscreen, RouteFlavour.AnimatedPage);
            host.Navigator.Push(animated);
            host.Settle();

            // Runs synchronously as far as the animation gate inside OnDestroy, and parks there.
            host.Navigator.TopmostRoute.Pop();

            Assert.AreEqual(
                ScreenState.Destroyed,
                animated.ScreenState,
                "the machine commits to Destroyed before waiting on the exit animation"
            );
            Assert.IsFalse(
                animated.PopTask.IsCompleted,
                "the pop is still waiting on the animation"
            );

            host.Unmount();
            host.PumpFrames(3);

            Assert.IsTrue(
                animated.PopTask.IsCompleted,
                "teardown must close out a route abandoned part-way through an ordinary destroy"
            );
        }

        /// <summary>
        ///     Teardown ends every route without chaining through the pause step that starts an exit
        ///     animation, so no route is left waiting for one.
        /// </summary>
        [Test]
        public void Unmount_EndsRoutesWithoutRunningTheExitAnimation()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();
            host.End();

            host.Begin("push B animated");
            host.Navigator.Push(
                host.Create("B", RouteModalType.Fullscreen, RouteFlavour.AnimatedPage)
            );
            host.Settle();
            host.End();

            host.Begin("unmount");
            host.Unmount();
            host.PumpFrames(3);

            // No OnDestroy of any kind: teardown goes straight to Destroyed, so there is no exit
            // animation to start and nothing to wait for. Disposal follows in widget order, which is the
            // order routes were pushed rather than stack order.
            host.AssertTrace(
                "mount A",
                "  > WillPush(A, null)",
                "  A OnInitialize",
                "  > DidPush(A, null)",
                "  A OnCreate",
                "  A OnResume",
                "  A OnFocus",
                "  stack: [A]",
                "push B animated",
                "  > WillPush(B, A)",
                "  B OnInitialize",
                "  A OnFocusLost",
                "  A OnPause",
                "  > DidPush(B, A)",
                "  B OnCreate",
                "  B OnResume",
                "  B OnFocus",
                "  stack: [B, A]",
                "unmount",
                "  B OnTeardown",
                "  A OnTeardown",
                "  A Dispose",
                "  B Dispose"
            );
        }

        /// <summary>
        ///     Answering a route that has already answered must be a no-op rather than a throw.
        /// </summary>
        /// <remarks>
        ///     Reachable directly, since ApplyScreenEvent is public and the Destroyed self-transition now
        ///     carries a handler. This is what the completer's TrySetResult buys: with a plain SetResult
        ///     the second completion would throw from inside a Zone callback, where nothing is positioned
        ///     to handle it.
        /// </remarks>
        [Test]
        public void Teardown_AfterAnOrdinaryDestroy_IsANoOp()
        {
            var trace = new NavigatorTrace();
            var route = new TracingRoute(trace, "R", RouteModalType.Fullscreen);

            route.ApplyScreenEvent(ScreenEvent.Create);
            route.ApplyScreenEvent(ScreenEvent.Destroy);

            Zone.Pump();

            Assert.IsTrue(route.PopTask.IsCompleted, "an ordinary destroy completes the pop");

            route.ApplyScreenEvent(ScreenEvent.Teardown);

            Zone.Pump();

            Assert.AreEqual(ScreenState.Destroyed, route.ScreenState);
            Assert.IsTrue(route.PopTask.IsCompleted);
            Assert.AreEqual(
                "  R OnCreate\n  R OnDestroy\n  R OnTeardown",
                trace.ToString(),
                "teardown still runs its handler; it is the completion that is idempotent"
            );
        }
    }
}
