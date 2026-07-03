using System.Collections.Generic;
using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // Regression coverage for ScrollListState.RequestBuildWindow/BuildWindow (ScrollList.cs): a
    // scroll-position tick that doesn't cross an item boundary must not re-invoke ItemBuilder for the
    // whole visible window, but an external [Atom] change read inside ItemBuilder must still force a
    // rebuild of the current window even when the window itself hasn't moved. Tests go straight
    // through ISliverState, bypassing RenderSliverList/the View entirely -- the fix lives entirely in
    // ScrollListState.
    public class ScrollListWindowTests
    {
        private static (ScrollList widget, ISliverState state, List<int> builtIndices) MountCountingList(
            int itemCount, MutableAtom<bool> externalFlag = null)
        {
            var builtIndices = new List<int>();

            var widget = new ScrollList
            {
                ItemCount = itemCount,
                ItemBuilder = (context, index) =>
                {
                    builtIndices.Add(index);
                    if (externalFlag != null)
                    {
                        _ = externalFlag.Value; // read inside ItemBuilder, mirroring a ViewModel's [Atom] read
                    }

                    return new FixedSizeBox { Key = Key.Of(index), Size = new Vector2(10, 10) };
                },
            };

            var state = (ISliverState) TestHarness.Mount(widget);
            return (widget, state, builtIndices);
        }

        [Test]
        public void RepeatedIdenticalWindow_DoesNotRebuild()
        {
            var (_, state, builtIndices) = MountCountingList(itemCount: 20);

            state.RequestBuildWindow(0, 5);
            state.RequestBuildWindow(0, 5);

            Assert.AreEqual(5, builtIndices.Count,
                "the second identical request should be served from cache, not re-invoke ItemBuilder");
        }

        [Test]
        public void WindowChange_RebuildsForNewRange()
        {
            var (_, state, builtIndices) = MountCountingList(itemCount: 20);

            state.RequestBuildWindow(0, 5);
            builtIndices.Clear();

            state.RequestBuildWindow(2, 7);

            CollectionAssert.AreEquivalent(new[] { 2, 3, 4, 5, 6 }, builtIndices);
        }

        [Test]
        public void ExternalAtomChange_WhileWindowStatic_ForcesRebuild()
        {
            var flag = Atom.Value(false);
            var (_, state, builtIndices) = MountCountingList(itemCount: 20, externalFlag: flag);

            state.RequestBuildWindow(0, 5);
            builtIndices.Clear();

            // Same window as before -- should be a pure cache hit.
            state.RequestBuildWindow(0, 5);
            Assert.AreEqual(0, builtIndices.Count,
                "an unchanged window must not re-invoke ItemBuilder");

            // This is the exact scenario that regressed during development: an external [Atom]
            // read inside ItemBuilder (e.g. a selection flag) changes, but the visible window
            // itself never moves. The rebuild must still happen.
            flag.Value = true;
            state.RequestBuildWindow(0, 5);

            Assert.AreEqual(5, builtIndices.Count,
                "an external atom change read inside ItemBuilder must force a rebuild even when the window doesn't move");
        }

        [Test]
        public void ItemBuilderSwap_IsPickedUpImmediately()
        {
            var (_, state, builtIndices) = MountCountingList(itemCount: 20);
            state.RequestBuildWindow(0, 5);
            builtIndices.Clear();

            var newBuiltIndices = new List<int>();
            var newWidget = new ScrollList
            {
                ItemCount = 20,
                ItemBuilder = (context, index) =>
                {
                    newBuiltIndices.Add(index);
                    return new FixedSizeBox { Key = Key.Of(index), Size = new Vector2(10, 10) };
                },
            };

            TestHarness.Update((State) state, newWidget);

            // Same range as before: with the old ItemBuilder this would've been served from cache.
            state.RequestBuildWindow(0, 5);

            Assert.AreEqual(5, newBuiltIndices.Count,
                "the new ItemBuilder must run for the current window even though the range didn't change, " +
                "since the ScrollList widget itself is a tracked dependency of the built window");
            Assert.AreEqual(0, builtIndices.Count, "the old ItemBuilder must not run again");
        }
    }
}
