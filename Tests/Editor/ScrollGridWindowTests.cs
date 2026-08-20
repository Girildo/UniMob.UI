using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // The grid shares ScrollList's reactive build-window bridge (VirtualizedChildren), so it must honor the
    // same contract: a window request that doesn't change the range is a cache hit, a moved window rebuilds
    // and evicts, and an external [Atom] read inside ItemBuilder forces a rebuild even when the range is
    // static. Mirrors ScrollListWindowTests, going straight through ISliverGridState.
    public class ScrollGridWindowTests
    {
        private static (
            ScrollGrid widget,
            ISliverGridState state,
            List<int> builtIndices
        ) MountCountingGrid(int itemCount, MutableAtom<bool> externalFlag = null)
        {
            var builtIndices = new List<int>();

            var widget = new ScrollGrid
            {
                CrossAxisCount = 2,
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

            var state = (ISliverGridState)TestHarness.Mount(widget);
            return (widget, state, builtIndices);
        }

        [Test]
        public void RepeatedIdenticalWindow_DoesNotRebuild()
        {
            var (_, state, builtIndices) = MountCountingGrid(itemCount: 20);

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
            var (_, state, builtIndices) = MountCountingGrid(itemCount: 20);

            state.RequestBuildWindow(0, 5);
            builtIndices.Clear();

            state.RequestBuildWindow(2, 7);

            CollectionAssert.AreEquivalent(new[] { 2, 3, 4, 5, 6 }, builtIndices);
        }

        [Test]
        public void WindowMovingAway_DeactivatesEvictedStates()
        {
            var (_, state, _) = MountCountingGrid(itemCount: 20);

            var firstWindow = state.RequestBuildWindow(0, 5);
            Assert.AreEqual(5, firstWindow.Length);
            Assert.IsFalse(
                firstWindow.Any(s => s.StateLifetime.IsDisposed),
                "just-built states must be alive"
            );

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
            var (_, state, builtIndices) = MountCountingGrid(itemCount: 20, externalFlag: flag);

            state.RequestBuildWindow(0, 5);
            builtIndices.Clear();

            state.RequestBuildWindow(0, 5);
            Assert.AreEqual(
                0,
                builtIndices.Count,
                "an unchanged window must not re-invoke ItemBuilder"
            );

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
            var (_, state, builtIndices) = MountCountingGrid(itemCount: 20);
            state.RequestBuildWindow(0, 5);
            builtIndices.Clear();

            var newBuiltIndices = new List<int>();
            var newWidget = new ScrollGrid
            {
                CrossAxisCount = 2,
                ItemCount = 20,
                ItemBuilder = (context, index) =>
                {
                    newBuiltIndices.Add(index);
                    return new FixedSizeBox { Key = Key.Of(index), Size = new Vector2(10, 10) };
                },
            };

            TestHarness.Update((State)state, newWidget);

            state.RequestBuildWindow(0, 5);

            Assert.AreEqual(
                5,
                newBuiltIndices.Count,
                "the new ItemBuilder must run for the current window even though the range didn't change"
            );
            Assert.AreEqual(0, builtIndices.Count, "the old ItemBuilder must not run again");
        }
    }
}
