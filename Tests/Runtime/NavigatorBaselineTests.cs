using System.Collections;
using NUnit.Framework;
using UniMob.UI.Widgets;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Pins what the navigator does today, so that a change to it is visible rather than assumed.
    /// </summary>
    /// <remarks>
    ///     These are characterization tests, not specifications. They assert current behaviour whether or
    ///     not that behaviour is desirable, and their value is entirely in the diff: a change that leaves
    ///     them untouched did not perturb navigation, and a change that moves a line has to justify
    ///     exactly that line. Where a trace records something arguably wrong, the comment says so rather
    ///     than the assertion being softened -- a baseline that only pins the behaviour its author
    ///     approved of is not a baseline.
    ///     <para>
    ///         <b>Golden traces are recorded, never reasoned out.</b> Write the scenario, run it, read the
    ///         pasteable actual from the failure message, and paste it in. A hand-derived trace asserts
    ///         its author's model of the code instead of the code, which is the one thing a baseline must
    ///         not do.
    ///     </para>
    ///     <para>
    ///         Each fixture asserts its whole trace from mount rather than only its own step. The
    ///         redundancy is deliberate: it makes every fixture readable end to end, and it means a change
    ///         to a shared step shows its full blast radius instead of one representative failure.
    ///     </para>
    ///     <para>
    ///         PlayMode only, and frame-driven: route completion defers through <c>Zone.NextFrame</c>, and
    ///         Zone only exists in play mode. See <see cref="NavigatorHost"/>.
    ///     </para>
    /// </remarks>
    public class NavigatorBaselineTests
    {
        [UnityTest]
        public IEnumerator Mount_InitialRoute_IsCreatedResumedAndFocused()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);

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
                "  stack: [A]");
        }

        /// <summary>
        ///     The incoming route is built before anything on screen is disturbed, so the screen the user
        ///     is looking at is not paused while the next one loads, and a route that fails to build
        ///     leaves the navigator untouched.
        /// </summary>
        [UnityTest]
        public IEnumerator PushFullscreenOverFullscreen_InitializesAboveBeforePausingBelow()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();
            host.End();

            host.Begin("push B fullscreen");
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
                "push B fullscreen",
                "  > WillPush(B, A)",
                "  B OnInitialize",
                "  A OnFocusLost",
                "  A OnPause",
                "  > DidPush(B, A)",
                "  B OnCreate",
                "  B OnResume",
                "  B OnFocus",
                "  stack: [B, A]");
        }

        /// <summary>
        ///     A popup only unfocuses what it covers. The route below stays resumed, which is what lets a
        ///     drawer sit over a live page.
        /// </summary>
        [UnityTest]
        public IEnumerator PushPopupOverFullscreen_UnfocusesBelowWithoutPausingIt()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();
            host.End();

            host.Begin("push B popup");
            host.Navigator.Push(host.Create("B", RouteModalType.Popup, RouteFlavour.Plain));
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
                "push B popup",
                "  > WillPush(B, A)",
                "  B OnInitialize",
                "  A OnFocusLost",
                "  > DidPush(B, A)",
                "  B OnCreate",
                "  B OnResume",
                "  B OnFocus",
                "  stack: [B, A]");
        }

        /// <summary>
        ///     Stacking popups: only the topmost is unfocused, and the fullscreen route two levels down is
        ///     left entirely alone.
        /// </summary>
        [UnityTest]
        public IEnumerator PushPopupOverPopup_UnfocusesOnlyTheTopmost()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();
            host.End();

            host.Begin("push B popup");
            host.Navigator.Push(host.Create("B", RouteModalType.Popup, RouteFlavour.Plain));
            yield return host.Settle();
            host.End();

            host.Begin("push C popup");
            host.Navigator.Push(host.Create("C", RouteModalType.Popup, RouteFlavour.Plain));
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
                "push B popup",
                "  > WillPush(B, A)",
                "  B OnInitialize",
                "  A OnFocusLost",
                "  > DidPush(B, A)",
                "  B OnCreate",
                "  B OnResume",
                "  B OnFocus",
                "  stack: [B, A]",
                "push C popup",
                "  > WillPush(C, B)",
                "  C OnInitialize",
                "  B OnFocusLost",
                "  > DidPush(C, B)",
                "  C OnCreate",
                "  C OnResume",
                "  C OnFocus",
                "  stack: [C, B, A]");
        }

        [UnityTest]
        public IEnumerator Pop_DestroysTopmost_ResumesBelow_ThenDisposesOnALaterFrame()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();
            host.End();

            host.Begin("push B fullscreen");
            host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();
            host.End();

            host.Begin("pop");
            host.Navigator.Pop();
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
                "push B fullscreen",
                "  > WillPush(B, A)",
                "  B OnInitialize",
                "  A OnFocusLost",
                "  A OnPause",
                "  > DidPush(B, A)",
                "  B OnCreate",
                "  B OnResume",
                "  B OnFocus",
                "  stack: [B, A]",
                "pop",
                "  > WillPop(B, A)",
                "  B OnFocusLost",
                "  B OnPause",
                "  B OnDestroy",
                "  A OnResume",
                "  > DidPop(B, A)",
                "  A OnFocus",
                "  B Dispose",
                "  stack: [A]");
        }

        /// <summary>
        ///     The navigator refuses to empty itself. PopInternal returns at depth 1 without touching the
        ///     route, and the trailing auto-focus is a no-op because the root is already focused.
        /// </summary>
        [UnityTest]
        public IEnumerator Pop_AtDepthOne_IsRefused()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();
            host.End();

            host.Begin("pop at depth 1");
            host.Navigator.Pop();
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
                "pop at depth 1",
                "  stack: [A]");
        }

        /// <summary>
        ///     Popping several routes at once resumes each one on its way past it, so an intermediate
        ///     route is resumed and then immediately paused and destroyed. That churn is real and is
        ///     pinned here deliberately; it is a consequence of UnpauseScreens running per iteration.
        /// </summary>
        [UnityTest]
        public IEnumerator PopTo_AcrossThreeRoutes_ResumesEachRouteBeforeDestroyingIt()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();
            host.End();

            var root = host.Navigator.TopmostRoute;

            host.Begin("push B fullscreen");
            host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();
            host.End();

            host.Begin("push C fullscreen");
            host.Navigator.Push(host.Create("C", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();
            host.End();

            host.Begin("popTo A");
            host.Navigator.PopTo(root);
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
                "push B fullscreen",
                "  > WillPush(B, A)",
                "  B OnInitialize",
                "  A OnFocusLost",
                "  A OnPause",
                "  > DidPush(B, A)",
                "  B OnCreate",
                "  B OnResume",
                "  B OnFocus",
                "  stack: [B, A]",
                "push C fullscreen",
                "  > WillPush(C, B)",
                "  C OnInitialize",
                "  B OnFocusLost",
                "  B OnPause",
                "  > DidPush(C, B)",
                "  C OnCreate",
                "  C OnResume",
                "  C OnFocus",
                "  stack: [C, B, A]",
                "popTo A",
                "  > WillPop(C, B)",
                "  C OnFocusLost",
                "  C OnPause",
                "  C OnDestroy",
                "  B OnResume",
                "  > DidPop(C, B)",
                "  > WillPop(B, A)",
                "  B OnPause",
                "  B OnDestroy",
                "  A OnResume",
                "  > DidPop(B, A)",
                "  A OnFocus",
                "  B Dispose",
                "  C Dispose",
                "  stack: [A]");
        }

        /// <summary>
        ///     A null target means "to the root", which PopToInternal reaches by never matching a key and
        ///     stopping at depth 1.
        /// </summary>
        [UnityTest]
        public IEnumerator PopToNull_PopsToTheRoot()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();
            host.End();

            host.Begin("push B fullscreen");
            host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();
            host.End();

            host.Begin("popTo null");

            // Null is a documented argument here, not an oversight: PopTo's command marks it CanBeNull
            // and reads it as "no target", which PopToInternal turns into "stop at the root".
            host.Navigator.PopTo(null!);
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
                "push B fullscreen",
                "  > WillPush(B, A)",
                "  B OnInitialize",
                "  A OnFocusLost",
                "  A OnPause",
                "  > DidPush(B, A)",
                "  B OnCreate",
                "  B OnResume",
                "  B OnFocus",
                "  stack: [B, A]",
                "popTo null",
                "  > WillPop(B, A)",
                "  B OnFocusLost",
                "  B OnPause",
                "  B OnDestroy",
                "  A OnResume",
                "  > DidPop(B, A)",
                "  A OnFocus",
                "  B Dispose",
                "  stack: [A]");
        }

        /// <summary>
        ///     Replace builds the incoming route before destroying the outgoing one, which is what makes
        ///     it atomic: an incoming route that fails to initialize leaves the stack untouched, where
        ///     previously the outgoing one was already destroyed and popped with nothing to replace it.
        /// </summary>
        [UnityTest]
        public IEnumerator Replace_AtDepthOne_InitializesNewBeforeDestroyingOld()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();
            host.End();

            host.Begin("replace with B");
            host.Navigator.Replace(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
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
                "replace with B",
                "  > WillReplace(B, A)",
                "  B OnInitialize",
                "  A OnFocusLost",
                "  A OnPause",
                "  A OnDestroy",
                "  > DidReplace(B, A)",
                "  B OnCreate",
                "  B OnResume",
                "  B OnFocus",
                "  A Dispose",
                "  stack: [B]");
        }

        /// <summary>
        ///     Replacing a fullscreen route with a popup resumes what was underneath, since it is about to
        ///     become visible again.
        /// </summary>
        [UnityTest]
        public IEnumerator Replace_FullscreenWithPopup_ResumesTheRouteBelow()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();
            host.End();

            host.Begin("push B fullscreen");
            host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();
            host.End();

            host.Begin("replace B with C popup");
            host.Navigator.Replace(host.Create("C", RouteModalType.Popup, RouteFlavour.Plain));
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
                "push B fullscreen",
                "  > WillPush(B, A)",
                "  B OnInitialize",
                "  A OnFocusLost",
                "  A OnPause",
                "  > DidPush(B, A)",
                "  B OnCreate",
                "  B OnResume",
                "  B OnFocus",
                "  stack: [B, A]",
                "replace B with C popup",
                "  > WillReplace(C, B)",
                "  C OnInitialize",
                "  B OnFocusLost",
                "  B OnPause",
                "  B OnDestroy",
                "  A OnResume",
                "  > DidReplace(C, B)",
                "  C OnCreate",
                "  C OnResume",
                "  C OnFocus",
                "  B Dispose",
                "  stack: [C, A]");
        }

        /// <summary>
        ///     Replacing a popup with a fullscreen route leaves the route below resumed, even though a
        ///     fullscreen route now covers it. A push would have paused it; replace only adjusts when the
        ///     outgoing route's modality differed from the incoming one's. Pinned as-is because it is
        ///     current behaviour, not because it looks right.
        /// </summary>
        [UnityTest]
        public IEnumerator Replace_PopupWithFullscreen_LeavesTheRouteBelowResumed()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();
            host.End();

            host.Begin("push B popup");
            host.Navigator.Push(host.Create("B", RouteModalType.Popup, RouteFlavour.Plain));
            yield return host.Settle();
            host.End();

            host.Begin("replace B with C fullscreen");
            host.Navigator.Replace(host.Create("C", RouteModalType.Fullscreen, RouteFlavour.Plain));
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
                "push B popup",
                "  > WillPush(B, A)",
                "  B OnInitialize",
                "  A OnFocusLost",
                "  > DidPush(B, A)",
                "  B OnCreate",
                "  B OnResume",
                "  B OnFocus",
                "  stack: [B, A]",
                "replace B with C fullscreen",
                "  > WillReplace(C, B)",
                "  C OnInitialize",
                "  B OnFocusLost",
                "  B OnPause",
                "  B OnDestroy",
                "  > DidReplace(C, B)",
                "  C OnCreate",
                "  C OnResume",
                "  C OnFocus",
                "  B Dispose",
                "  stack: [C, A]");
        }

        /// <summary>
        ///     NewRoot is a PopTo(null) and a Replace issued as one command batch, so the trailing
        ///     auto-focus runs once at the end rather than after each half.
        /// </summary>
        [UnityTest]
        public IEnumerator NewRoot_PopsToRootThenReplacesIt()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();
            host.End();

            host.Begin("push B fullscreen");
            host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain));
            yield return host.Settle();
            host.End();

            host.Begin("newRoot C");
            host.Navigator.NewRoot(host.Create("C", RouteModalType.Fullscreen, RouteFlavour.Plain));
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
                "push B fullscreen",
                "  > WillPush(B, A)",
                "  B OnInitialize",
                "  A OnFocusLost",
                "  A OnPause",
                "  > DidPush(B, A)",
                "  B OnCreate",
                "  B OnResume",
                "  B OnFocus",
                "  stack: [B, A]",
                "newRoot C",
                "  > WillPop(B, A)",
                "  B OnFocusLost",
                "  B OnPause",
                "  B OnDestroy",
                "  A OnResume",
                "  > DidPop(B, A)",
                "  > WillReplace(C, A)",
                "  C OnInitialize",
                "  A OnPause",
                "  A OnDestroy",
                "  > DidReplace(C, A)",
                "  C OnCreate",
                "  C OnResume",
                "  C OnFocus",
                "  A Dispose",
                "  B Dispose",
                "  stack: [C]");
        }

        /// <summary>
        ///     The one flavour whose ending spans frames. OnDestroy is entered synchronously, the routes
        ///     below are resumed while it is still waiting, and the stack does not change until the exit
        ///     animation has finished -- which is what makes a popped route report ScreenState.Destroyed
        ///     while still on the stack (defect 3).
        /// </summary>
        [UnityTest]
        public IEnumerator Pop_AnimatedPage_AwaitsExitAnimationBeforeMutatingTheStack()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();
            host.End();

            host.Begin("push B animated");
            host.Navigator.Push(host.Create("B", RouteModalType.Fullscreen, RouteFlavour.AnimatedPage));
            yield return host.Settle();
            host.End();

            host.Begin("pop animated");
            host.Navigator.Pop();
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
                "pop animated",
                "  > WillPop(B, A)",
                "  B OnFocusLost",
                "  B OnPause",
                "  B OnDestroy enter",
                "  A OnResume",
                "  B OnDestroy exit",
                "  > DidPop(B, A)",
                "  A OnFocus",
                "  B Dispose",
                "  stack: [A]");
        }

        /// <summary>
        ///     The two tasks a route hands its callers. PushTask completes a frame after the route is
        ///     initialized; PopTask completes only once the route has actually been destroyed, which is
        ///     the contract Phase 2's teardown fix exists to keep on the one path that currently breaks it.
        /// </summary>
        [UnityTest]
        public IEnumerator PushAndPop_CompleteTheRoutesTasks()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var pushed = host.Create("B", RouteModalType.Fullscreen, RouteFlavour.Plain);

            host.Navigator.Push(pushed);
            yield return host.Settle();

            Assert.IsTrue(pushed.PushTask.IsCompleted, "PushTask should complete once the route is pushed");
            Assert.IsFalse(pushed.PopTask.IsCompleted, "PopTask should not complete while the route is on the stack");

            host.Navigator.Pop();
            yield return host.Settle();

            Assert.IsTrue(pushed.PopTask.IsCompleted, "PopTask should complete once the route is popped");
            Assert.IsTrue(pushed.DisposeTask.IsCompleted, "DisposeTask should complete once the route leaves the tree");
        }
    }
}
