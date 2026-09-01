using System.Threading.Tasks;
using NUnit.Framework;
using UniMob.UI.Navigation;
using UniMob.UI.Widgets;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Covers <see cref="NavigatorState.IsNavigating"/>, the predicate a harness waits on before it
    ///     looks at the page.
    /// </summary>
    /// <remarks>
    ///     The window these fixtures exist for is the one a push spends initializing its incoming route.
    ///     Nothing else the navigator publishes moves during it, and the clock has nothing queued either,
    ///     so a harness with only those to go on acts on the page the user is leaving.
    /// </remarks>
    public class NavigatorIsNavigatingTests : NavigatorFixture
    {
        [Test]
        public void ASettledNavigator_IsNotNavigating()
        {
            var host = NavigatorHost.Mount("root", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            Assert.That(host.Navigator.IsNavigating, Is.False);
        }

        /// <summary>
        ///     The gap this member exists for. A route is initialized before it is pushed, so throughout
        ///     its initialization the stack is the stack it was, the topmost route is the route it was,
        ///     and that route is in the state it was in.
        /// </summary>
        [Test]
        public void APushWhoseRouteIsStillInitializing_IsNavigating_ThoughNothingElseHasMoved()
        {
            var host = NavigatorHost.Mount("root", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var atRest = PublicView(host.Navigator);
            var initializing = new TaskCompletionSource<object>();

            host.Navigator.Push(new GatedRoute("slow", initGate: initializing.Task));

            // The clock is what a harness would otherwise wait on, and it has nothing left to do: the
            // route is parked on a task, which is not a frame callback and not a queued reaction.
            host.Settle();

            Assert.That(
                host.Navigator.IsNavigating,
                Is.True,
                "the push has not landed, whatever the clock says"
            );
            Assert.That(
                PublicView(host.Navigator),
                Is.EqualTo(atRest),
                "and nothing else the navigator publishes has moved"
            );

            Zone.PumpFrames(5);

            Assert.That(
                host.Navigator.IsNavigating,
                Is.True,
                "frames passing is not the same as the push finishing"
            );
            Assert.That(PublicView(host.Navigator), Is.EqualTo(atRest));

            initializing.SetResult(null);
            host.Settle();

            Assert.That(host.Navigator.IsNavigating, Is.False);
            Assert.That(host.Navigator.NavigationStack.Count, Is.EqualTo(2));
            Assert.That(host.Navigator.TopmostRoute.Key, Is.EqualTo("slow"));
        }

        /// <summary>
        ///     A route's entry and exit transitions are awaited inside the command loop, so a page that
        ///     is still animating out has not finished leaving.
        /// </summary>
        [Test]
        public void APop_IsNavigating_UntilTheExitTransitionEnds()
        {
            var host = NavigatorHost.Mount("root", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var page = host.Create("page", RouteModalType.Fullscreen, RouteFlavour.AnimatedPage);
            host.Navigator.Push(page);
            host.Settle();

            Assert.That(host.Navigator.IsNavigating, Is.False, "the push has landed");

            page.Pop();

            Assert.That(
                host.Navigator.IsNavigating,
                Is.True,
                "a command is queued the moment it is issued, before any frame runs"
            );

            Zone.PumpFrames(2);

            Assert.That(
                host.Navigator.IsNavigating,
                Is.True,
                "the exit animation is awaited inside the command loop"
            );

            host.Settle();

            Assert.That(host.Navigator.IsNavigating, Is.False);
            Assert.That(host.Navigator.NavigationStack.Count, Is.EqualTo(1));
        }

        /// <summary>
        ///     A pop request is answered outside the command loop, so nothing is queued and nothing is
        ///     running while the route decides. The request holds the route's slot for that whole window.
        /// </summary>
        [Test]
        public void APopRequestStillBeingDecided_IsNavigating()
        {
            var host = NavigatorHost.Mount("root", RouteModalType.Fullscreen, RouteFlavour.Plain);
            host.Settle();

            var deciding = new TaskCompletionSource<object>();
            var asked = new GatedRoute("asked", popGate: deciding.Task);

            host.Navigator.Push(asked);
            host.Settle();
            Assert.That(host.Navigator.IsNavigating, Is.False);

            var outcome = host.Navigator.RequestPop(asked, request: this);
            host.Settle();

            Assert.That(
                host.Navigator.IsNavigating,
                Is.True,
                "the route is still deciding, and a decision is work the stack cannot show"
            );
            Assert.That(
                PublicView(host.Navigator),
                Does.StartWith("2 routes"),
                "the route being asked is still on the stack, unchanged"
            );

            deciding.SetResult(null);
            host.Settle();

            Assert.That(host.Navigator.IsNavigating, Is.False);
            Assert.That(outcome.IsCompleted, Is.True);
            Assert.That(host.Navigator.NavigationStack.Count, Is.EqualTo(1));
        }

        /// <summary>
        ///     Everything about a navigator that is readable without this member: how deep it is, what is
        ///     on top, what state that route is in, and how many screens are rendered.
        /// </summary>
        private static string PublicView(NavigatorState navigator)
        {
            var depth = 0;
            Route topmost = null;

            foreach (var route in navigator.NavigationStack)
            {
                topmost ??= route;
                depth++;
            }

            return depth
                + " routes, top "
                + (topmost == null ? "none" : topmost.Key + " " + topmost.ScreenState)
                + ", "
                + navigator.Screens.Length
                + " screens";
        }

        /// <summary>
        ///     A route that parks where a fixture tells it to, for as long as the fixture holds the gate.
        /// </summary>
        private sealed class GatedRoute : Route
        {
            private readonly Task _initGate;
            private readonly Task _popGate;

            public GatedRoute(string key, Task initGate = null, Task popGate = null)
                : base(new RouteSettings(key, RouteModalType.Fullscreen))
            {
                _initGate = initGate;
                _popGate = popGate;
            }

            public override Widget Build(BuildContext context) => new Empty();

            protected override async Task OnInitialize()
            {
                if (_initGate != null)
                {
                    await _initGate;
                }

                await base.OnInitialize();
            }

            protected override async Task<PopDecision> OnPopRequested(object request)
            {
                if (_popGate != null)
                {
                    await _popGate;
                }

                return await base.OnPopRequested(request);
            }
        }
    }
}
