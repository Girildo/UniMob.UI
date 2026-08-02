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

            // Atom-backed, matching ScrollList's [Atom] ScrollPixelOffset. A plain field would be read
            // during sizing without registering as a dependency, so a scroll would not invalidate the
            // memoized layout and the fixture would silently stop converging.
            private readonly MutableAtom<float> _scrollPixelOffset = Atom.Value(0f);

            public float ScrollPixelOffset
            {
                get => _scrollPixelOffset.Value;
                set => _scrollPixelOffset.Value = value;
            }
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
                return BuildWindow?.Invoke(startIndexInclusive, endIndexExclusive)
                    ?? Array.Empty<IState>();
            }
        }

        private static IState Box(float mainAxisSize) =>
            TestHarness.Mount(new FixedSizeBox { Size = new Vector2(0, mainAxisSize) });

        // Horizontal-axis box: main-axis size on x (the cross axis y gets stretched to the viewport by the list).
        private static IState HBox(float mainAxisSize) =>
            TestHarness.Mount(new FixedSizeBox { Size = new Vector2(mainAxisSize, 0) });

        [Test]
        public void Eager_TotalContentSize_SumsChildrenPlusSpacing()
        {
            var state = new FakeSliverState
            {
                AllChildren = new[] { Box(10), Box(20), Box(30) },
                Spacing = 5,
            };

            var render = new RenderSliverList(state);
            render.Layout(new LayoutConstraints(0, 0, 100, 1000));

            Assert.AreEqual(70f, render.TotalContentSize(), 0.01f); // 10+20+30 + 5*2
        }

        [Test]
        public void TotalContentSize_ExcludesVirtualizationCacheExtent()
        {
            // The cache extent warms an off-screen build window; it is NOT scrollable content. Adding it to the
            // content size used to leave a dead-zone of empty scroll past the last item (max scroll is derived
            // from the content rect, which the View sizes to TotalContentSize()).
            var state = new FakeSliverState
            {
                AllChildren = new[] { Box(10), Box(20), Box(30) },
                Spacing = 5,
                VirtualizationCacheExtent = 500,
            };

            var render = new RenderSliverList(state);
            render.Layout(new LayoutConstraints(0, 0, 100, 1000));

            Assert.AreEqual(70f, render.TotalContentSize(), 0.01f); // 10+20+30 + 5*2 -- and crucially NOT + 500
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
            render.Layout(new LayoutConstraints(0, 0, 100, 20)); // viewport = [15, 35)

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
                BuildWindow = (start, end) =>
                    Enumerable.Range(start, end - start).Select(_ => Box(999)).ToArray(),
            };

            var render = new RenderSliverList(state);
            render.Layout(new LayoutConstraints(0, 0, 100, 200)); // viewport = [120, 320)

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
            render.Layout(new LayoutConstraints(0, 0, 100, 100)); // viewport main axis = 100

            Assert.AreEqual(
                100f,
                render.CalculateScrollPixelOffset(2, ScrollToPosition.Start),
                0.01f
            );
            Assert.AreEqual(
                75f,
                render.CalculateScrollPixelOffset(2, ScrollToPosition.Center),
                0.01f
            );
            Assert.AreEqual(50f, render.CalculateScrollPixelOffset(2, ScrollToPosition.End), 0.01f);
        }

        [Test]
        public void Lazy_LastItemTrailingEdge_CoincidesWithContentSize_AfterMeasuringVariedSizes()
        {
            // Item 0 is a tall header (300); every other item is 50. An item's real extent only becomes
            // known once the window containing it is built, so after scrolling past the header the running
            // average diverges from the exact extents already recorded for items above the window. That
            // divergence used to be applied to the content size (TotalContentSize, exact-measured model) but
            // NOT to the positioning anchor (uniform-stride model), pushing the final item's trailing edge
            // past the content rect the view is sized to -- so it rendered under the RectMask2D and was
            // clipped, with no way to scroll far enough to reveal it (max scroll == content size - viewport).
            float SizeOf(int i) => i == 0 ? 300f : 50f;

            var state = new FakeSliverState
            {
                ItemCount = 50,
                VirtualizationCacheExtent = 0,
                BuildWindow = (start, end) =>
                    Enumerable.Range(start, end - start).Select(i => Box(SizeOf(i))).ToArray(),
            };

            var render = new RenderSliverList(state);
            var viewport = new LayoutConstraints(0, 0, 100, 200);

            // First lay out at the top so the header (and a couple of small items) get measured, seeding the
            // average well above the 50px that dominates the rest of the list.
            state.ScrollPixelOffset = 0f;
            render.Layout(viewport);

            // Then hold at the bottom -- clamping to the current content size exactly as the ScrollRect does --
            // until the estimate settles. The estimate starts biased high (from the header) and relaxes toward
            // 50 over several passes; keep laying out at the (moving) bottom so it converges.
            for (var pass = 0; pass < 80; pass++)
            {
                state.ScrollPixelOffset = Mathf.Max(0f, render.TotalContentSize() - 200f);
                render.Layout(viewport);
            }

            var visible = state.LastVisibleChildren;
            Assert.IsNotEmpty(
                visible,
                "the bottom of the list must have visible items once the estimate settles"
            );

            var last = visible[visible.Count - 1];
            Assert.AreEqual(
                49,
                last.ChildIndex,
                "the final item must be in the window when scrolled to the bottom"
            );

            var trailingEdge = last.Layout.Position.y + last.Layout.Size.y;
            Assert.AreEqual(
                render.TotalContentSize(),
                trailingEdge,
                1f,
                "the final item's trailing edge must coincide with the content size the view is sized to, "
                    + "otherwise it renders past the mask and is clipped"
            );
        }

        [Test]
        public void CalculateScrollPixelOffset_LazyWithItemExtent_StartCenterEnd()
        {
            var state = new FakeSliverState
            {
                ItemCount = 10,
                ItemExtent = 50,
                BuildWindow = (start, end) =>
                    Enumerable.Range(start, end - start).Select(_ => Box(50)).ToArray(),
            };

            var render = new RenderSliverList(state);
            render.Layout(new LayoutConstraints(0, 0, 100, 100));

            Assert.AreEqual(
                200f,
                render.CalculateScrollPixelOffset(4, ScrollToPosition.Start),
                0.01f
            );
            Assert.AreEqual(
                175f,
                render.CalculateScrollPixelOffset(4, ScrollToPosition.Center),
                0.01f
            );
            Assert.AreEqual(
                150f,
                render.CalculateScrollPixelOffset(4, ScrollToPosition.End),
                0.01f
            );
        }

        // --- Horizontal axis: every branch above is axis-conditional (constraints, culling, positioning,
        //     total size, scroll-to), and the rest of the fixture only exercises the vertical side. ---

        [Test]
        public void Horizontal_CullsAndPositionsAlongXAxis()
        {
            var state = new FakeSliverState
            {
                Axis = Axis.Horizontal,
                AllChildren = new[] { HBox(10), HBox(10), HBox(10) }, // occupy x [0,10) [10,20) [20,30)
                ScrollPixelOffset = 15,
                VirtualizationCacheExtent = 0,
            };

            var render = new RenderSliverList(state);
            // A horizontal list must have a bounded width; width == viewport main axis == 20 -> viewport x [15, 35).
            render.Layout(new LayoutConstraints(0, 0, 20, 100));

            var visible = state.LastVisibleChildren;
            CollectionAssert.AreEqual(new[] { 1, 2 }, visible.Select(v => v.ChildIndex).ToArray());

            // Offsets advance along x (y stays 0), and each child is stretched to the cross axis (height 100).
            Assert.AreEqual(new Vector2(10, 0), visible[0].Layout.Position);
            Assert.AreEqual(new Vector2(20, 0), visible[1].Layout.Position);
            Assert.AreEqual(new Vector2(10, 100), visible[0].Layout.Size);
        }

        [Test]
        public void Horizontal_TotalContentSize_SumsChildrenPlusSpacing()
        {
            var state = new FakeSliverState
            {
                Axis = Axis.Horizontal,
                AllChildren = new[] { HBox(10), HBox(20), HBox(30) },
                Spacing = 5,
            };

            var render = new RenderSliverList(state);
            render.Layout(new LayoutConstraints(0, 0, 1000, 100));

            Assert.AreEqual(70f, render.TotalContentSize(), 0.01f); // 10+20+30 + 5*2, summed on the x-axis
        }

        [Test]
        public void CalculateScrollPixelOffset_Horizontal_StartCenterEnd()
        {
            var state = new FakeSliverState
            {
                Axis = Axis.Horizontal,
                AllChildren = new[] { HBox(50), HBox(50), HBox(50), HBox(50), HBox(50) },
            };

            var render = new RenderSliverList(state);
            render.Layout(new LayoutConstraints(0, 0, 100, 100)); // viewport main axis (width) = 100

            Assert.AreEqual(
                100f,
                render.CalculateScrollPixelOffset(2, ScrollToPosition.Start),
                0.01f
            );
            Assert.AreEqual(
                75f,
                render.CalculateScrollPixelOffset(2, ScrollToPosition.Center),
                0.01f
            );
            Assert.AreEqual(50f, render.CalculateScrollPixelOffset(2, ScrollToPosition.End), 0.01f);
        }

        [Test]
        public void CalculateScrollPixelOffset_LazyMeasured_UsesExactPrefix_StartCenterEnd()
        {
            // No ItemExtent -> extents are measured, and the target's offset must come from the exact per-item
            // prefix (EstimateLeadingEdgeOffset over _measuredExtents), NOT a uniform average*index -- the path
            // rewritten alongside the drift fix. A huge cache builds & measures the whole list in one pass, so
            // every extent is exact and the math is fully deterministic.
            var sizes = new[] { 10f, 20f, 30f, 40f, 50f };
            var state = new FakeSliverState
            {
                ItemCount = sizes.Length,
                VirtualizationCacheExtent = 10000,
                BuildWindow = (start, end) =>
                    Enumerable.Range(start, end - start).Select(i => Box(sizes[i])).ToArray(),
            };

            var render = new RenderSliverList(state);
            render.Layout(new LayoutConstraints(0, 0, 100, 50)); // viewport main axis = 50

            // Item 3's exact leading edge is 10+20+30 = 60 (a uniform average would give 3*30 = 90); extent 40.
            // Content = 150, viewport = 50 -> scrollable = 100, so none of these clamp.
            Assert.AreEqual(
                60f,
                render.CalculateScrollPixelOffset(3, ScrollToPosition.Start),
                0.01f
            );
            Assert.AreEqual(
                55f,
                render.CalculateScrollPixelOffset(3, ScrollToPosition.Center),
                0.01f
            ); // 60 - (50-40)/2
            Assert.AreEqual(50f, render.CalculateScrollPixelOffset(3, ScrollToPosition.End), 0.01f); // 60 - (50-40)
        }
    }
}
