using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // Covers RenderSliverList's own layout math directly (eager sizing/culling, lazy window
    // computation, scroll-to-index pixel math). ScrollListWindowTests already covers
    // ScrollListState's reactive build-window contract -- this fixture is the RenderObject's own,
    // separate contract, using a plain (non-reactive) fake so the two concerns don't overlap.
    public class RenderSliverListTests
    {
        private class FakeSliverState : FakeState, ISliverState
        {
            public IState[] Children => AllChildren; // Unused by RenderSliverList itself; only to satisfy IMultiChildLayoutState.
            public IState[] AllChildren { get; set; } = Array.Empty<IState>();
            public int? ItemCount { get; set; }
            public float? ItemExtent { get; set; }
            public Axis Axis { get; set; } = Axis.Vertical;
            public float ScrollPixelOffset { get; set; }
            public float? VirtualizationCacheExtent { get; set; } = 0f;
            public float Spacing { get; set; }

            public List<IndexedLayoutData> LastVisibleChildren { get; private set; }
            public (int start, int end)? LastRequestedWindow { get; private set; }
            public Func<int, int, IState[]> BuildWindow { get; set; }

            public void SetVisibleChildren(List<IndexedLayoutData> visibleChildren)
            {
                LastVisibleChildren = new List<IndexedLayoutData>(visibleChildren);
            }

            public IState[] RequestBuildWindow(int startIndexInclusive, int endIndexExclusive)
            {
                LastRequestedWindow = (startIndexInclusive, endIndexExclusive);
                return BuildWindow?.Invoke(startIndexInclusive, endIndexExclusive) ?? Array.Empty<IState>();
            }
        }

        private static IState Box(float mainAxisSize) => TestHarness.Mount(new FixedSizeBox { Size = new Vector2(0, mainAxisSize) });

        [Test]
        public void Eager_TotalContentSize_SumsChildrenPlusSpacing()
        {
            var state = new FakeSliverState
            {
                AllChildren = new[] { Box(10), Box(20), Box(30) },
                Spacing = 5,
            };

            var render = new RenderSliverList(state);
            render.PerformLayoutImmediate(new LayoutConstraints(0, 0, 100, 1000));

            Assert.AreEqual(70f, render.TotalContentSize(), 0.01f); // 10+20+30 + 5*2
        }

        [Test]
        public void Eager_Culling_OnlyKeepsChildrenIntersectingViewportPlusCache()
        {
            var state = new FakeSliverState
            {
                AllChildren = new[] { Box(10), Box(10), Box(10) }, // occupy [0,10) [10,20) [20,30)
                ScrollPixelOffset = 15,
                VirtualizationCacheExtent = 0,
            };

            var render = new RenderSliverList(state);
            render.PerformLayoutImmediate(new LayoutConstraints(0, 0, 100, 20)); // viewport = [15, 35)

            var visibleIndices = state.LastVisibleChildren.Select(v => v.ChildIndex).ToArray();
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, visibleIndices);
        }

        [Test]
        public void Lazy_RequestsTheWindowCoveringViewportPlusCache()
        {
            var state = new FakeSliverState
            {
                ItemCount = 100,
                ItemExtent = 50,
                ScrollPixelOffset = 120,
                VirtualizationCacheExtent = 0,
                BuildWindow = (start, end) => Enumerable.Range(start, end - start).Select(_ => Box(999)).ToArray(),
            };

            var render = new RenderSliverList(state);
            render.PerformLayoutImmediate(new LayoutConstraints(0, 0, 100, 200)); // viewport = [120, 320)

            // stride = 50; startIndex = floor(120/50) = 2; endIndex = ceil(320/50) = 7.
            Assert.AreEqual((2, 7), state.LastRequestedWindow);
        }

        [Test]
        public void CalculateScrollPixelOffset_Eager_StartCenterEnd()
        {
            var state = new FakeSliverState
            {
                AllChildren = new[] { Box(50), Box(50), Box(50), Box(50), Box(50) },
            };

            var render = new RenderSliverList(state);
            render.PerformLayoutImmediate(new LayoutConstraints(0, 0, 100, 100)); // viewport main axis = 100

            Assert.AreEqual(100f, render.CalculateScrollPixelOffset(2, ScrollToPosition.Start), 0.01f);
            Assert.AreEqual(75f, render.CalculateScrollPixelOffset(2, ScrollToPosition.Center), 0.01f);
            Assert.AreEqual(50f, render.CalculateScrollPixelOffset(2, ScrollToPosition.End), 0.01f);
        }

        [Test]
        public void CalculateScrollPixelOffset_LazyWithItemExtent_StartCenterEnd()
        {
            var state = new FakeSliverState
            {
                ItemCount = 10,
                ItemExtent = 50,
                BuildWindow = (start, end) => Enumerable.Range(start, end - start).Select(_ => Box(50)).ToArray(),
            };

            var render = new RenderSliverList(state);
            render.PerformLayoutImmediate(new LayoutConstraints(0, 0, 100, 100));

            Assert.AreEqual(200f, render.CalculateScrollPixelOffset(4, ScrollToPosition.Start), 0.01f);
            Assert.AreEqual(175f, render.CalculateScrollPixelOffset(4, ScrollToPosition.Center), 0.01f);
            Assert.AreEqual(150f, render.CalculateScrollPixelOffset(4, ScrollToPosition.End), 0.01f);
        }
    }
}
