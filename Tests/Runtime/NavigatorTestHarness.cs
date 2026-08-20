using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UniMob.UI.Internal;
using UniMob.UI.Navigation;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     What kind of route to build. The flavours differ only in how a route ends, which is the axis
    ///     every navigator behaviour under test turns on.
    /// </summary>
    /// <remarks>
    ///     <see cref="Plain"/> is a bare <see cref="Route"/> with no animation, so its whole lifecycle
    ///     runs synchronously. <see cref="InstantPage"/> is a <see cref="PageRoute"/> with zero duration:
    ///     <see cref="AnimationController.Forward"/> short-circuits a zero duration without ever adding a
    ///     Zone ticker, so it never actually animates either. <see cref="AnimatedPage"/> is the only
    ///     flavour whose exit is deferred across frames, and therefore the only one that can be observed
    ///     part-way through being destroyed.
    /// </remarks>
    public enum RouteFlavour
    {
        Plain,
        InstantPage,
        AnimatedPage,

        /// <summary>
        ///     A plain route whose destroy handler throws immediately, without yielding first.
        /// </summary>
        ThrowsOnDestroy,

        /// <summary>
        ///     A plain route whose destroy handler throws after yielding, which is the shape of the only
        ///     failure the package produces on its own: <see cref="PageRoute"/>'s exit-animation wait
        ///     being cancelled by disposal.
        /// </summary>
        /// <remarks>
        ///     Kept distinct from <see cref="ThrowsOnDestroy"/> so a fixture can hold the two to the same
        ///     outcome. They used to differ entirely: a transition only awaited a handler's task when it
        ///     was not already completed, and a synchronously faulted task is completed, so failing
        ///     without yielding meant failing invisibly.
        /// </remarks>
        ThrowsAsyncOnDestroy,
    }

    /// <summary>
    ///     An ordered, human-readable record of everything a navigator did, asserted as a whole.
    /// </summary>
    /// <remarks>
    ///     Deliberately one flat sequence rather than a per-route grouping. What these fixtures exist to
    ///     protect is the <i>interleaving</i> between routes -- which route is paused before which other
    ///     route is initialized -- and a grouped format hides exactly that. A single sequence also means a
    ///     behaviour change shows up as a diff you read, instead of as one failed assertion partway down a
    ///     list that tells you nothing about what else moved.
    /// </remarks>
    public sealed class NavigatorTrace
    {
        private readonly List<string> _lines = new List<string>();

        /// <summary>
        ///     How many entries have been recorded. Used to detect quiescence while pumping frames.
        /// </summary>
        public int Count => _lines.Count;

        /// <summary>
        ///     How many route handlers have been entered but not yet left.
        /// </summary>
        /// <remarks>
        ///     Silence is not the same as being finished. An animated route's <c>OnDestroy</c> parks on its
        ///     exit animation for as many frames as the animation lasts, recording nothing throughout, so a
        ///     purely silence-based quiescence check would declare the navigator settled part-way through a
        ///     pop and truncate the trace. Counting entered-but-unfinished handlers closes that gap using
        ///     only what the tracing routes already see, with no production API added for the tests' benefit.
        /// </remarks>
        public int Pending { get; private set; }

        public void Enter()
        {
            Pending++;
        }

        public void Leave()
        {
            Pending--;
        }

        public void Command(string description)
        {
            _lines.Add(description);
        }

        public void Event(string routeKey, string what)
        {
            _lines.Add("  " + routeKey + " " + what);
        }

        /// <summary>
        ///     Records an observer callback, marked so it reads apart from a route's own lifecycle.
        /// </summary>
        /// <remarks>
        ///     Into the same sequence as everything else on purpose. Where a callback fires relative to
        ///     each <c>OnPause</c>, <c>OnDestroy</c> and stack mutation is the whole of what
        ///     <see cref="INavigatorObserver"/> promises, and only one interleaved trace can state it.
        /// </remarks>
        public void Observer(string call)
        {
            _lines.Add("  > " + call);
        }

        public void Stack(NavigatorState navigator)
        {
            var keys = new List<string>();

            // NavigationStack enumerates a Stack<Route>, which yields topmost first.
            foreach (var route in navigator.NavigationStack)
            {
                keys.Add(route.Key);
            }

            _lines.Add("  stack: [" + string.Join(", ", keys) + "]");
        }

        public string[] Lines => _lines.ToArray();

        public override string ToString()
        {
            return string.Join("\n", _lines);
        }
    }

    /// <summary>
    ///     The recording half of a tracing route, factored out because C# has single inheritance and the
    ///     two tracing routes must derive from <see cref="Route"/> and <see cref="PageRoute"/> separately.
    /// </summary>
    internal sealed class RouteTracer
    {
        private readonly NavigatorTrace _trace;
        private readonly string _key;

        public RouteTracer(NavigatorTrace trace, string key)
        {
            _trace = trace;
            _key = key;
        }

        public void Record(string what)
        {
            _trace.Event(_key, what);
        }

        public void Enter(string what)
        {
            _trace.Enter();
            _trace.Event(_key, what);
        }

        public void Leave(string what)
        {
            _trace.Event(_key, what);
            _trace.Leave();
        }
    }

    /// <summary>
    ///     How a tracing route's destroy handler fails, if at all. Both failing modes must now look the
    ///     same to the navigator; the enum exists so a fixture can prove it.
    /// </summary>
    internal enum DestroyFailure
    {
        None,
        Synchronous,
        Asynchronous,
    }

    internal sealed class TracingRoute : Route
    {
        private readonly RouteTracer _tracer;
        private readonly DestroyFailure _destroyFailure;

        public TracingRoute(
            NavigatorTrace trace,
            string key,
            RouteModalType modalType,
            DestroyFailure destroyFailure = DestroyFailure.None
        )
            : base(new RouteSettings(key, modalType))
        {
            _tracer = new RouteTracer(trace, key);
            _destroyFailure = destroyFailure;
        }

        public override Widget Build(BuildContext context) => new Empty();

        protected override Task OnInitialize()
        {
            _tracer.Record("OnInitialize");
            return base.OnInitialize();
        }

        protected override Task OnCreate()
        {
            _tracer.Record("OnCreate");
            return base.OnCreate();
        }

        protected override Task OnResume()
        {
            _tracer.Record("OnResume");
            return base.OnResume();
        }

        protected override Task OnFocus()
        {
            _tracer.Record("OnFocus");
            return base.OnFocus();
        }

        protected override Task OnFocusLost()
        {
            _tracer.Record("OnFocusLost");
            return base.OnFocusLost();
        }

        protected override Task OnPause()
        {
            _tracer.Record("OnPause");
            return base.OnPause();
        }

        // Thrown before base.OnDestroy in both failing cases, so the pop completer is never set either:
        // the route has failed to end in every sense a caller can observe.
        protected override async Task OnDestroy()
        {
            _tracer.Record("OnDestroy");

            if (_destroyFailure == DestroyFailure.Asynchronous)
            {
                // Yields first, so the returned task is still incomplete when the transition inspects it.
                // That difference used to decide whether the failure was seen at all.
                await Task.Yield();
                throw new InvalidOperationException("route failed to destroy");
            }

            if (_destroyFailure == DestroyFailure.Synchronous)
            {
                throw new InvalidOperationException("route failed to destroy");
            }

            await base.OnDestroy();
        }

        protected override Task OnTeardown()
        {
            _tracer.Record("OnTeardown");
            return base.OnTeardown();
        }

        public override void Dispose()
        {
            _tracer.Record("Dispose");
            base.Dispose();
        }
    }

    internal sealed class TracingPageRoute : PageRoute
    {
        private readonly RouteTracer _tracer;

        public TracingPageRoute(
            NavigatorTrace trace,
            string key,
            RouteModalType modalType,
            float duration
        )
            : base(new RouteSettings(key, modalType), duration, duration)
        {
            _tracer = new RouteTracer(trace, key);
        }

        protected override Widget BuildPage(
            BuildContext context,
            AnimationController animation,
            AnimationController secondaryAnimation
        ) => new Empty();

        protected override Widget BuildTransitions(
            BuildContext context,
            AnimationController animation,
            AnimationController secondaryAnimation,
            Widget child
        ) => child;

        protected override Task OnInitialize()
        {
            _tracer.Record("OnInitialize");
            return base.OnInitialize();
        }

        protected override Task OnCreate()
        {
            _tracer.Record("OnCreate");
            return base.OnCreate();
        }

        protected override Task OnResume()
        {
            _tracer.Record("OnResume");
            return base.OnResume();
        }

        protected override Task OnFocus()
        {
            _tracer.Record("OnFocus");
            return base.OnFocus();
        }

        protected override Task OnFocusLost()
        {
            _tracer.Record("OnFocusLost");
            return base.OnFocusLost();
        }

        protected override Task OnPause()
        {
            _tracer.Record("OnPause");
            return base.OnPause();
        }

        /// <summary>
        ///     Records entry and exit separately, because for an animated page these are frames apart:
        ///     PageRoute.OnDestroy awaits the exit animation before completing PopTask, and that gap is
        ///     the thing several fixtures are about.
        /// </summary>
        protected override async Task OnDestroy()
        {
            _tracer.Enter("OnDestroy enter");

            try
            {
                await base.OnDestroy();
            }
            catch (OperationCanceledException)
            {
                // The route's lifetime died while its exit animation was still running, so Atom.When
                // cancelled and base.OnDestroy never completed the pop. Recorded rather than swallowed:
                // this is a real ending, and one a trace should show as itself rather than as a fixture
                // that mysteriously waits out its frame budget.
                _tracer.Leave("OnDestroy canceled");
                throw;
            }

            _tracer.Leave("OnDestroy exit");
        }

        /// <summary>
        ///     Not overridden by <see cref="PageRoute"/> itself, which is the point: teardown skips the
        ///     animation gate entirely, so unlike OnDestroy there is no enter/exit pair to record.
        /// </summary>
        protected override Task OnTeardown()
        {
            _tracer.Record("OnTeardown");
            return base.OnTeardown();
        }

        public override void Dispose()
        {
            _tracer.Record("Dispose");
            base.Dispose();
        }
    }

    /// <summary>
    ///     Writes every observer callback into the trace, so the goldens pin where each one fires relative
    ///     to the lifecycle handlers around it.
    /// </summary>
    internal sealed class TracingObserver : INavigatorObserver
    {
        private readonly NavigatorTrace _trace;

        public TracingObserver(NavigatorTrace trace)
        {
            _trace = trace;
        }

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
            _trace.Observer(callback + "(" + Name(route) + ", " + Name(other) + ")");
        }

        private static string Name(Route route) => route == null ? "null" : route.Key;
    }

    /// <summary>
    ///     Mounts a real <see cref="Navigator"/> and drives it a frame at a time, recording what happens.
    /// </summary>
    /// <remarks>
    ///     PlayMode only. <c>Route.Initialize</c>/<c>Dispose</c>/<c>OnDestroy</c> all defer through
    ///     <c>Zone.Current.NextFrame</c>, and Zone sets <c>Current</c> from a
    ///     <c>[RuntimeInitializeOnLoadMethod]</c> that only runs in play mode, so an EditMode fixture would
    ///     null-reference before asserting anything.
    ///     <para>
    ///         <see cref="Settle"/> must reconcile the child state tree on every frame, not just pump
    ///         them. Routes are disposed through <c>Builder.OnDispose</c>, which only fires when the
    ///         navigator's child states are re-reconciled and the departed route's widget is deactivated.
    ///         Nothing does that unless something reads <c>NavigatorState.Screens</c>, which is what a real
    ///         frame's render pass would do. Without it a popped route is never disposed and the trace
    ///         quietly omits it.
    ///     </para>
    /// </remarks>
    public sealed class NavigatorHost
    {
        /// <summary>
        ///     Long enough that an exit animation cannot complete inside a single frame, so an animated
        ///     route is genuinely observable mid-destroy, and short enough that fixtures stay quick.
        /// </summary>
        public const float AnimatedDuration = 0.25f;

        private const int QuietFramesRequired = 3;
        private const int MaxFrames = 300;

        private readonly string _rootKey;
        private readonly Dictionary<string, Func<Route>> _routes;
        private readonly TracingObserver _recorder;

        private NavigatorHost(
            NavigatorTrace trace,
            NavigatorState navigator,
            string rootKey,
            Dictionary<string, Func<Route>> routes,
            TracingObserver recorder
        )
        {
            Trace = trace;
            Navigator = navigator;
            _rootKey = rootKey;
            _routes = routes;
            _recorder = recorder;
        }

        public NavigatorTrace Trace { get; }

        public NavigatorState Navigator { get; }

        /// <summary>
        ///     Mounts a navigator whose observers are the trace recorder plus whatever a fixture supplies.
        /// </summary>
        /// <remarks>
        ///     The recorder has to exist before the widget does, because the initial route is pushed from
        ///     <c>InitState</c> and an observer supplied at construction is expected to hear it.
        /// </remarks>
        public static NavigatorHost Mount(
            string rootKey,
            RouteModalType rootModal,
            RouteFlavour rootFlavour,
            params INavigatorObserver[] observers
        )
        {
            var trace = new NavigatorTrace();
            trace.Command("mount " + rootKey);

            var routes = new Dictionary<string, Func<Route>>
            {
                { rootKey, () => Build(trace, rootKey, rootModal, rootFlavour) },
            };

            var recorder = new TracingObserver(trace);
            var widget = CreateWidget(rootKey, routes, recorder, observers);

            var state = (NavigatorState)TestHarness.Mount(widget);
            return new NavigatorHost(trace, state, rootKey, routes, recorder);
        }

        /// <summary>
        ///     Replaces the navigator's widget with one carrying a different observer list, as a parent
        ///     rebuilding with different configuration would.
        /// </summary>
        /// <remarks>
        ///     A whole new widget rather than a mutated list, because that is the only way observers can
        ///     change: they are configuration, and the navigator reads them off whichever widget is
        ///     current. Same key and type, so the state is updated in place and the stack survives.
        /// </remarks>
        public void Rebuild(params INavigatorObserver[] observers)
        {
            Trace.Command("rebuild with new observers");
            TestHarness.Update(Navigator, CreateWidget(_rootKey, _routes, _recorder, observers));
        }

        private static Navigator CreateWidget(
            string rootKey,
            Dictionary<string, Func<Route>> routes,
            TracingObserver recorder,
            INavigatorObserver[] observers
        )
        {
            var composed = new List<INavigatorObserver> { recorder };
            composed.AddRange(observers);

            return new Navigator(rootKey, routes, composed);
        }

        /// <summary>
        ///     Builds a route bound to this host's trace, for pushing or replacing during a fixture.
        /// </summary>
        public Route Create(string key, RouteModalType modalType, RouteFlavour flavour)
        {
            return Build(Trace, key, modalType, flavour);
        }

        private static Route Build(
            NavigatorTrace trace,
            string key,
            RouteModalType modalType,
            RouteFlavour flavour
        )
        {
            switch (flavour)
            {
                case RouteFlavour.Plain:
                    return new TracingRoute(trace, key, modalType);

                case RouteFlavour.InstantPage:
                    return new TracingPageRoute(trace, key, modalType, 0f);

                case RouteFlavour.AnimatedPage:
                    return new TracingPageRoute(trace, key, modalType, AnimatedDuration);

                case RouteFlavour.ThrowsOnDestroy:
                    return new TracingRoute(trace, key, modalType, DestroyFailure.Synchronous);

                case RouteFlavour.ThrowsAsyncOnDestroy:
                    return new TracingRoute(trace, key, modalType, DestroyFailure.Asynchronous);

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(flavour),
                        flavour,
                        "Unknown route flavour"
                    );
            }
        }

        /// <summary>
        ///     Opens a new section of the trace. Call before issuing a navigator command.
        /// </summary>
        public void Begin(string description)
        {
            Trace.Command(description);
        }

        /// <summary>
        ///     Closes a section by recording the resulting stack. Call after <see cref="Settle"/>.
        /// </summary>
        public void End()
        {
            Trace.Stack(Navigator);
        }

        /// <summary>
        ///     Pumps frames until the trace stops growing, so a fixture never has to guess a frame count.
        /// </summary>
        /// <remarks>
        ///     Quiescence is measured in consecutive silent frames rather than a single one, because the
        ///     lifecycle legitimately goes quiet for a frame mid-sequence: a completion scheduled through
        ///     <c>Zone.NextFrame</c> lands on the following frame, and the work it unblocks lands on the
        ///     one after that.
        /// </remarks>
        public IEnumerator Settle()
        {
            var quiet = 0;

            for (var frame = 0; frame < MaxFrames; frame++)
            {
                var before = Trace.Count;

                yield return null;

                Reconcile();

                quiet = Trace.Count == before ? quiet + 1 : 0;

                // Both conditions are required. Pending alone would pass before an operation has even
                // started, and silence alone ends a pop while its exit animation is still running.
                if (quiet >= QuietFramesRequired && Trace.Pending == 0)
                {
                    yield break;
                }
            }

            // Reaching the cap is a finding, not a flake: something entered a handler it never left, or
            // kept the navigator busy indefinitely. Say so rather than letting the fixture assert against
            // a half-recorded trace and fail somewhere less informative.
            Assert.Fail(
                "Navigator did not settle within "
                    + MaxFrames
                    + " frames (pending handlers: "
                    + Trace.Pending
                    + ").\n\n--- trace so far ---\n"
                    + Trace
            );
        }

        /// <summary>
        ///     Does what a frame's render pass does to the navigator's children, which is what actually
        ///     disposes a route whose widget has left the stack.
        /// </summary>
        private void Reconcile()
        {
            _ = Navigator.Screens;
        }

        /// <summary>
        ///     Removes the navigator from the state tree, as a parent rebuilding without it would.
        /// </summary>
        public void Unmount()
        {
            using (Atom.NoWatch)
            {
                StateUtilities.DeactivateChild(Navigator);
            }
        }

        /// <summary>
        ///     Advances frames without reconciling, for use once the navigator has been unmounted and its
        ///     child states no longer exist to be read.
        /// </summary>
        /// <remarks>
        ///     Zone is a DontDestroyOnLoad behaviour independent of the state tree, so callbacks queued
        ///     through <c>Zone.NextFrame</c> during disposal still run after the navigator is gone. That is
        ///     precisely what completing a route's PopTask at teardown depends on.
        /// </remarks>
        public IEnumerator PumpFrames(int count)
        {
            for (var frame = 0; frame < count; frame++)
            {
                yield return null;
            }
        }

        /// <summary>
        ///     Asserts the whole recorded trace, reporting a mismatch as a line-by-line diff plus the
        ///     pasteable actual.
        /// </summary>
        /// <remarks>
        ///     The failure message matters more than usual here. These traces are recorded rather than
        ///     reasoned out, so the common reason to see one fail is that a change moved behaviour on
        ///     purpose and the golden value now needs replacing. Printing the actual in pasteable form
        ///     makes that a copy, and printing the first differing line makes it obvious whether the
        ///     movement was the intended one.
        /// </remarks>
        public void AssertTrace(params string[] expected)
        {
            var actual = Trace.Lines;

            if (Matches(expected, actual))
            {
                return;
            }

            var message = new StringBuilder();
            message.AppendLine("Navigator trace did not match.");
            message.AppendLine(FirstDifference(expected, actual));
            message.AppendLine();
            message.AppendLine("--- expected ---");
            message.AppendLine(string.Join("\n", expected));
            message.AppendLine();
            message.AppendLine("--- actual ---");
            message.AppendLine(Trace.ToString());
            message.AppendLine();
            message.AppendLine("--- actual, pasteable ---");
            message.Append(Pasteable(actual));

            Assert.Fail(message.ToString());
        }

        private static bool Matches(string[] expected, string[] actual)
        {
            if (expected.Length != actual.Length)
            {
                return false;
            }

            for (var i = 0; i < expected.Length; i++)
            {
                if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string FirstDifference(string[] expected, string[] actual)
        {
            var shared = Math.Min(expected.Length, actual.Length);

            for (var i = 0; i < shared; i++)
            {
                if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
                {
                    return "First difference at line "
                        + (i + 1)
                        + "\n  expected: "
                        + expected[i]
                        + "\n  actual:   "
                        + actual[i];
                }
            }

            return expected.Length > actual.Length
                ? "Trace ended early at line " + (shared + 1) + ", expected: " + expected[shared]
                : "Trace ran long at line " + (shared + 1) + ", actual: " + actual[shared];
        }

        private static string Pasteable(string[] lines)
        {
            var text = new StringBuilder();
            text.AppendLine("AssertTrace(");

            for (var i = 0; i < lines.Length; i++)
            {
                text.Append("    \"").Append(lines[i]).Append('"');
                text.AppendLine(i == lines.Length - 1 ? ");" : ",");
            }

            return text.ToString();
        }

        /// <summary>
        ///     Prints the recorded trace as pasteable C# arguments.
        /// </summary>
        /// <remarks>
        ///     A golden trace is recorded, never predicted. Write a fixture with this call in place of
        ///     <see cref="AssertTrace"/>, run it once, paste the output back, and the assertion then
        ///     describes what the navigator does rather than what its author assumed it does.
        /// </remarks>
        public void DumpTrace()
        {
            Debug.Log(Pasteable(Trace.Lines));
        }
    }
}
