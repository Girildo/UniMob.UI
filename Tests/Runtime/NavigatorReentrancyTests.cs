using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UniMob.UI.Widgets;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Covers the rule that a navigation raised from inside the command loop joins its queue rather
    ///     than starting a second loop over the same stack.
    /// </summary>
    /// <remarks>
    ///     The loop is started only when one is not already running, but "already running" used to be read
    ///     off the task the loop returns -- and that task is not assigned until the loop reaches its first
    ///     real await. Every step of a navigation whose handlers complete synchronously runs before that
    ///     point, which is every navigation that does not animate. Anything navigating from inside that
    ///     window found the field still holding the previous, completed loop and started a second one on
    ///     top of the first.
    ///     <para>
    ///         The two loops then interleave over one stack. The push below is the plainest symptom: the
    ///         inner push completes entirely while the outer one is still building its route, so the outer
    ///         route lands on top of the inner one and the stack ends up in the opposite order to the one
    ///         the pushes were issued in.
    ///     </para>
    ///     <para>
    ///         <c>OnInitialize</c> is used to reach it because it is the earliest hook the package offers,
    ///         and it needs nothing else to exist. <see cref="NavigatorObserverTests"/> pins the same rule
    ///         from the observer side, where a callback is the thing doing the navigating.
    ///     </para>
    /// </remarks>
    public class NavigatorReentrancyTests
    {
        [UnityTest]
        public IEnumerator PushingFromOnInitialize_IsQueuedBehindThePushThatCausedIt()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var inner = host.Create("C", RouteModalType.Fullscreen, RouteFlavour.Plain);
            var outer = new PushingOnInitializeRoute("B", () => host.Navigator.Push(inner));

            host.Navigator.Push(outer);
            yield return host.Settle();

            Assert.AreEqual(3, host.Navigator.NavigationStack.Count);
            Assert.AreEqual(
                "C",
                host.Navigator.TopmostRoute.Key,
                "the route pushed from inside B's initialization must end up above B, not below it"
            );

            CollectionAssert.AreEqual(
                new[] { "C", "B", "A" },
                Keys(host),
                "the stack is in the order the pushes were issued, topmost first"
            );
        }

        /// <summary>
        ///     Two routes queued from inside one initialization still arrive in the order they were issued.
        /// </summary>
        [UnityTest]
        public IEnumerator SeveralPushesFromOnInitialize_ArriveInTheOrderTheyWereIssued()
        {
            var host = NavigatorHost.Mount("A", RouteModalType.Fullscreen, RouteFlavour.Plain);
            yield return host.Settle();

            var first = host.Create("C", RouteModalType.Fullscreen, RouteFlavour.Plain);
            var second = host.Create("D", RouteModalType.Fullscreen, RouteFlavour.Plain);

            var outer = new PushingOnInitializeRoute(
                "B",
                () =>
                {
                    host.Navigator.Push(first);
                    host.Navigator.Push(second);
                }
            );

            host.Navigator.Push(outer);
            yield return host.Settle();

            CollectionAssert.AreEqual(new[] { "D", "C", "B", "A" }, Keys(host));
        }

        private static string[] Keys(NavigatorHost host)
        {
            var stack = host.Navigator.NavigationStack;
            var keys = new string[stack.Count];
            var index = 0;

            // NavigationStack enumerates a Stack<Route>, which yields topmost first.
            foreach (var route in stack)
            {
                keys[index++] = route.Key;
            }

            return keys;
        }

        /// <summary>
        ///     Navigates from its own initialization, which is the earliest point anything can navigate
        ///     from inside the loop.
        /// </summary>
        private sealed class PushingOnInitializeRoute : Route
        {
            private readonly Action _navigate;

            public PushingOnInitializeRoute(string key, Action navigate)
                : base(new RouteSettings(key, RouteModalType.Fullscreen))
            {
                _navigate = navigate;
            }

            public override Widget Build(BuildContext context) => new Empty();

            protected override Task OnInitialize()
            {
                _navigate();
                return base.OnInitialize();
            }
        }
    }
}
