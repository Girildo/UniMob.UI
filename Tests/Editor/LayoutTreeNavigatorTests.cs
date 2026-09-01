using System;
using System.Collections.Generic;
using NUnit.Framework;
using UniMob.UI.Diagnostics;
using UniMob.UI.Navigation;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     The navigator case of <see cref="LayoutTree.ChildrenOf"/>: the walk reaches every screen on
    ///     the stack, not only the route the navigator was mounted with.
    /// </summary>
    /// <remarks>
    ///     A navigator is the root of every page, so a walk that stopped at one would describe a whole
    ///     app as a single node. <c>NavigatorState</c> satisfies <see cref="IMultiChildLayoutState"/> as
    ///     well, which is why this is pinned rather than assumed: that is the layout path's projection
    ///     and is free to narrow to a window the way the scrolling lists' does, while
    ///     <see cref="INavigatorState.Screens"/> is the whole stack by contract.
    /// </remarks>
    public class LayoutTreeNavigatorTests : NavigatorFixture
    {
        private static Route Screen(string key, string label) =>
            new RouteBuilder(
                new RouteSettings(key, RouteModalType.Fullscreen),
                _ => new LabelledBox { WidgetLabel = label }
            );

        private NavigatorState MountNavigator()
        {
            var routes = new Dictionary<string, Func<Route>>
            {
                { "root", () => Screen("root", "root screen") },
            };

            var navigator = (NavigatorState)TestHarness.Mount(new Navigator("root", routes));

            SettleTree(navigator);
            return navigator;
        }

        // Reconciles on every frame, as the render pass does. Nothing builds a route's state unless
        // something reads Screens, so a pushed route would otherwise never reach the tree at all.
        private void SettleTree(NavigatorState navigator) =>
            Zone.Settle(onFrame: () => _ = navigator.Screens);

        [Test]
        public void ChildrenOf_ANavigator_ReturnsEveryScreen_NotOnlyTheTopmost()
        {
            var navigator = MountNavigator();

            navigator.Push(Screen("pushed", "pushed screen"));
            SettleTree(navigator);

            var children = LayoutTree.ChildrenOf(navigator);

            Assert.That(
                children.Count,
                Is.EqualTo(2),
                "the covered route is still on the stack and still part of the tree"
            );
        }

        [Test]
        public void Describe_ReachesAWidgetOnTheInitialRoute()
        {
            var navigator = MountNavigator();

            StringAssert.Contains("\"root screen\"", LayoutTree.Describe(navigator));
        }

        [Test]
        public void Describe_ReachesAWidgetOnAPushedRoute()
        {
            var navigator = MountNavigator();

            navigator.Push(Screen("pushed", "pushed screen"));
            SettleTree(navigator);

            var described = LayoutTree.Describe(navigator);

            StringAssert.Contains("\"root screen\"", described);
            StringAssert.Contains("\"pushed screen\"", described);
        }
    }
}
