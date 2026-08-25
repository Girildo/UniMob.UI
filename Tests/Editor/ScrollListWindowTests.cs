using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
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
        private static (
            ScrollList widget,
            ISliverState state,
            List<int> builtIndices
        ) MountCountingList(int itemCount, MutableAtom<bool> externalFlag = null)
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

            var state = (ISliverState)TestHarness.Mount(widget);
            return (widget, state, builtIndices);
        }

        [Test]
        public void RepeatedIdenticalWindow_DoesNotRebuild()
        {
            var (_, state, builtIndices) = MountCountingList(itemCount: 20);

            state.RequestBuildWindow(0, 5);
            state.RequestBuildWindow(0, 5);

            Assert.AreEqual(
                5,
                builtIndices.Count,
                "the second identical request should be served from cache, not re-invoke ItemBuilder"
            );
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
        public void WindowMovingAway_DeactivatesEvictedStates()
        {
            var (_, state, _) = MountCountingList(itemCount: 20);

            var firstWindow = state.RequestBuildWindow(0, 5); // States for indices 0..4.
            Assert.AreEqual(5, firstWindow.Length);
            Assert.IsFalse(
                firstWindow.Any(s => s.StateLifetime.IsDisposed),
                "just-built states must be alive"
            );

            // Move the window entirely past the first range: 0..4 fall outside [10,15) and must be evicted.
            // This is the manually-managed _builtStates cache's job -- if eviction ever stops disposing, the
            // States (and their reactive subscriptions) leak for the life of the list.
            state.RequestBuildWindow(10, 15);

            Assert.IsTrue(
                firstWindow.All(s => s.StateLifetime.IsDisposed),
                "states that left the build window must be deactivated (disposed), not leaked"
            );
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
            Assert.AreEqual(
                0,
                builtIndices.Count,
                "an unchanged window must not re-invoke ItemBuilder"
            );

            // This is the exact scenario that regressed during development: an external [Atom]
            // read inside ItemBuilder (e.g. a selection flag) changes, but the visible window
            // itself never moves. The rebuild must still happen.
            flag.Value = true;
            state.RequestBuildWindow(0, 5);

            Assert.AreEqual(
                5,
                builtIndices.Count,
                "an external atom change read inside ItemBuilder must force a rebuild even when the window doesn't move"
            );
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

            TestHarness.Update((State)state, newWidget);

            // Same range as before: with the old ItemBuilder this would've been served from cache.
            state.RequestBuildWindow(0, 5);

            Assert.AreEqual(
                5,
                newBuiltIndices.Count,
                "the new ItemBuilder must run for the current window even though the range didn't change, "
                    + "since the ScrollList widget itself is a tracked dependency of the built window"
            );
            Assert.AreEqual(0, builtIndices.Count, "the old ItemBuilder must not run again");
        }

        // A lazy list whose items carry keys must reconcile by those keys, the way the eager Children
        // path does: a State follows its key to a new slot instead of being rebuilt because the slot it
        // sat in now shows a different key. The builder below reads a mutable id list, so a test can
        // insert, remove or reorder items and rebuild the same window.
        private static (ISliverState state, System.Action rebuild) MountKeyedList(List<int> ids)
        {
            ScrollList Build() =>
                new ScrollList
                {
                    ItemCount = ids.Count,
                    ItemBuilder = (context, index) =>
                        new FixedSizeBox { Key = Key.Of(ids[index]), Size = new Vector2(10, 10) },
                };

            var state = (ISliverState)TestHarness.Mount(Build());
            return (state, () => TestHarness.Update((State)state, Build()));
        }

        [Test]
        public void ItemInsertedAboveWindow_KeepsTheStatesOfTheKeysStillInWindow()
        {
            var ids = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var (state, rebuild) = MountKeyedList(ids);

            var before = state.RequestBuildWindow(0, 5); // keys 0..4 at slots 0..4

            ids.Insert(0, 100);
            rebuild();
            var after = state.RequestBuildWindow(0, 5); // keys 100,0,1,2,3 at slots 0..4

            Assert.AreEqual(Key.Of(100), after[0].Key, "the inserted item occupies slot 0");
            for (var i = 0; i < 4; i++)
            {
                Assert.AreSame(
                    before[i],
                    after[i + 1],
                    $"key {i} moved from slot {i} to slot {i + 1} and must keep its State"
                );
            }

            Assert.IsFalse(
                after.Any(s => s.StateLifetime.IsDisposed),
                "no State in the new window may be disposed"
            );
            Assert.IsTrue(
                before[4].StateLifetime.IsDisposed,
                "key 4 was pushed out of the window and must be deactivated"
            );
        }

        [Test]
        public void ItemRemovedAboveWindow_KeepsTheStatesOfTheKeysStillInWindow()
        {
            var ids = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var (state, rebuild) = MountKeyedList(ids);

            var before = state.RequestBuildWindow(0, 5); // keys 0..4 at slots 0..4

            ids.RemoveAt(0);
            rebuild();
            var after = state.RequestBuildWindow(0, 5); // keys 1..5 at slots 0..4

            for (var i = 1; i < 5; i++)
            {
                Assert.AreSame(
                    before[i],
                    after[i - 1],
                    $"key {i} moved from slot {i} to slot {i - 1} and must keep its State"
                );
            }

            Assert.AreEqual(Key.Of(5), after[4].Key, "key 5 entered the window at slot 4");
            Assert.IsFalse(
                before.Contains(after[4]),
                "key 5 was never built before and must be a fresh State"
            );
            Assert.IsTrue(
                before[0].StateLifetime.IsDisposed,
                "the removed key 0 must be deactivated"
            );
        }

        [Test]
        public void ItemsReorderedWithinWindow_MoveTheirStatesToTheNewSlots()
        {
            var ids = new List<int> { 0, 1, 2, 3, 4 };
            var (state, rebuild) = MountKeyedList(ids);

            var before = state.RequestBuildWindow(0, 5);

            ids.Reverse();
            rebuild();
            var after = state.RequestBuildWindow(0, 5);

            for (var i = 0; i < 5; i++)
            {
                Assert.AreSame(
                    before[i],
                    after[4 - i],
                    $"key {i} moved from slot {i} to slot {4 - i} and must keep its State"
                );
            }

            Assert.IsFalse(
                before.Any(s => s.StateLifetime.IsDisposed),
                "a reorder disposes nothing: every key is still in the window"
            );
        }

        [Test]
        public void UnkeyedItems_KeepTheStateAtTheirSlot()
        {
            var widget = new ScrollList
            {
                ItemCount = 10,
                ItemBuilder = (context, index) => new FixedSizeBox { Size = new Vector2(10, 10) },
            };
            var state = (ISliverState)TestHarness.Mount(widget);

            var before = state.RequestBuildWindow(0, 5);
            state.RequestBuildWindow(2, 7);
            var after = state.RequestBuildWindow(0, 5);

            for (var i = 2; i < 5; i++)
            {
                Assert.AreSame(
                    before[i],
                    after[i],
                    $"slot {i} stayed inside the window throughout and must keep its State"
                );
            }

            Assert.IsTrue(
                before[0].StateLifetime.IsDisposed && before[1].StateLifetime.IsDisposed,
                "slots 0 and 1 left the window in between and must have been deactivated"
            );
            Assert.IsFalse(
                after.Any(s => s.StateLifetime.IsDisposed),
                "no State in the current window may be disposed"
            );
        }
    }
}
