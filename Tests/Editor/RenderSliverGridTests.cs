using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // Covers RenderSliverGrid's own layout math directly (fixed vs measured rows, culling, positioning,
    // total content size, scroll-to), mirroring RenderSliverListTests. Uses a plain (non-reactive) fake so
    // this stays the RenderObject's contract; ScrollGridWindowTests covers the reactive build-window path.
    public class RenderSliverGridTests
    {
        private class FakeSliverGridState : FakeState, ISliverGridState
        {
            public IState[] Children => AllChildren; // Only to satisfy IMultiChildLayoutState; unused by the render object.
            public IState[] AllChildren { get; set; } = Array.Empty<IState>();
            public int? ItemCount { get; set; }
            public Axis Axis { get; set; } = Axis.Vertical;

            // Atom-backed, matching ScrollGrid's [Atom] ScrollPixelOffset. A plain field would be read
            // during sizing without registering as a dependency, so a scroll would not invalidate the
            // memoized layout and the fixture would silently stop converging.
            private readonly MutableAtom<float> _scrollPixelOffset = Atom.Value(0f);

            public float ScrollPixelOffset
            {
                get => _scrollPixelOffset.Value;
                set => _scrollPixelOffset.Value = value;
            }
            public float? VirtualizationCacheExtent { get; set; } = 0f;
            public SliverGridDelegate GridDelegate { get; set; }
            public RectPadding Padding { get; set; }

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

        // Vertical grid: main axis is y. The cross (x) is forced to the cell width, so only the main size matters.
        private static IState VCell(float mainSize) =>
            TestHarness.Mount(new FixedSizeBox { Size = new Vector2(0, mainSize) });

        // Horizontal grid: main axis is x.
        private static IState HCell(float mainSize) =>
            TestHarness.Mount(new FixedSizeBox { Size = new Vector2(mainSize, 0) });

        private static Vector2 PositionOf(FakeSliverGridState state, int childIndex) =>
            state.LastVisibleChildren.First(v => v.ChildIndex == childIndex).Layout.Position;

        private static Vector2 SizeOf(FakeSliverGridState state, int childIndex) =>
            state.LastVisibleChildren.First(v => v.ChildIndex == childIndex).Layout.Size;

        private static int[] VisibleIndices(FakeSliverGridState state) =>
            state.LastVisibleChildren.Select(v => v.ChildIndex).OrderBy(i => i).ToArray();

        // --- Fixed main-axis mode (delegate dictates the cell size; positions are exact) ---

        [Test]
        public void Fixed_Vertical_PositionsInGridAndTotalContentSize()
        {
            var state = new FakeSliverGridState
            {
                // 2 columns, 50px tall cells, 10px between rows.
                GridDelegate = new SliverGridDelegateWithFixedCrossAxisCount(
                    2,
                    mainAxisSpacing: 10f,
                    mainAxisExtent: 50f
                ),
                AllChildren = Enumerable.Range(0, 5).Select(_ => VCell(0)).ToArray(),
            };

            var render = new RenderSliverGrid(state);
            render.Layout(new LayoutConstraints(0, 0, 200, 200)); // cellCross = 100

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, VisibleIndices(state));

            Assert.AreEqual(new Vector2(0, 0), PositionOf(state, 0));
            Assert.AreEqual(new Vector2(100, 0), PositionOf(state, 1));
            Assert.AreEqual(new Vector2(0, 60), PositionOf(state, 2)); // row 1 => 50 + 10
            Assert.AreEqual(new Vector2(100, 60), PositionOf(state, 3));
            Assert.AreEqual(new Vector2(0, 120), PositionOf(state, 4)); // row 2 => 2*(50+10)

            Assert.AreEqual(new Vector2(100, 50), SizeOf(state, 0));

            // 3 rows * 50 + 2 gaps * 10 = 170.
            Assert.AreEqual(170f, render.TotalContentSize(), 0.01f);
        }

        [Test]
        public void Fixed_Vertical_CullsRowsOutsideViewport()
        {
            var state = new FakeSliverGridState
            {
                GridDelegate = new SliverGridDelegateWithFixedCrossAxisCount(
                    2,
                    mainAxisSpacing: 10f,
                    mainAxisExtent: 50f
                ),
                AllChildren = Enumerable.Range(0, 5).Select(_ => VCell(0)).ToArray(),
                ScrollPixelOffset = 65,
            };

            var render = new RenderSliverGrid(state);
            render.Layout(new LayoutConstraints(0, 0, 200, 50)); // viewport main = [65, 115)

            // row 0 @ [0,50), row 1 @ [60,110), row 2 @ [120,170) -> only row 1 (items 2,3) intersects.
            CollectionAssert.AreEqual(new[] { 2, 3 }, VisibleIndices(state));
        }

        [Test]
        public void Fixed_Vertical_MaxCrossAxisExtent_DrivesColumnCount()
        {
            var state = new FakeSliverGridState
            {
                // 250 / 100 -> 3 columns.
                GridDelegate = new SliverGridDelegateWithMaxCrossAxisExtent(
                    100f,
                    mainAxisExtent: 50f
                ),
                AllChildren = Enumerable.Range(0, 4).Select(_ => VCell(0)).ToArray(),
            };

            var render = new RenderSliverGrid(state);
            render.Layout(new LayoutConstraints(0, 0, 250, 400));

            // With 3 columns, items 0..2 share row 0 (main 0); item 3 wraps to row 1 (main > 0).
            Assert.AreEqual(0f, PositionOf(state, 0).y, 0.01f);
            Assert.AreEqual(0f, PositionOf(state, 1).y, 0.01f);
            Assert.AreEqual(0f, PositionOf(state, 2).y, 0.01f);
            Assert.Greater(PositionOf(state, 3).y, 0f);
        }

        [Test]
        public void Fixed_Horizontal_PositionsAlongXAndTotalContentSize()
        {
            var state = new FakeSliverGridState
            {
                Axis = Axis.Horizontal,
                GridDelegate = new SliverGridDelegateWithFixedCrossAxisCount(
                    2,
                    mainAxisSpacing: 10f,
                    mainAxisExtent: 50f
                ),
                AllChildren = Enumerable.Range(0, 4).Select(_ => HCell(0)).ToArray(),
            };

            var render = new RenderSliverGrid(state);
            // Horizontal grid: width is the main axis, height is the cross axis -> cellCross = 100.
            render.Layout(new LayoutConstraints(0, 0, 200, 200));

            Assert.AreEqual(new Vector2(0, 0), PositionOf(state, 0));
            Assert.AreEqual(new Vector2(0, 100), PositionOf(state, 1)); // second column stacks along y
            Assert.AreEqual(new Vector2(60, 0), PositionOf(state, 2)); // second row advances along x
            Assert.AreEqual(new Vector2(60, 100), PositionOf(state, 3));

            Assert.AreEqual(new Vector2(50, 100), SizeOf(state, 0)); // (mainExtent, cellCross)

            // 2 rows * 50 + 1 gap * 10 = 110.
            Assert.AreEqual(110f, render.TotalContentSize(), 0.01f);
        }

        [Test]
        public void Fixed_Lazy_RequestsRowWindowCoveringViewportPlusCache()
        {
            var state = new FakeSliverGridState
            {
                GridDelegate = new SliverGridDelegateWithFixedCrossAxisCount(
                    2,
                    mainAxisExtent: 50f
                ),
                ItemCount = 100,
                ScrollPixelOffset = 120,
                VirtualizationCacheExtent = 0,
                BuildWindow = (start, end) =>
                    Enumerable.Range(start, end - start).Select(_ => VCell(0)).ToArray(),
            };

            var render = new RenderSliverGrid(state);
            render.Layout(new LayoutConstraints(0, 0, 200, 200)); // viewport main = [120, 320)

            // rowStride = 50; startRow = floor(120/50) = 2; endRow = ceil(320/50) = 7.
            // 2 columns -> item window [4, 14).
            Assert.AreEqual((4, 14), state.LastRequestedWindow);
        }

        // --- Measured rows mode (no fixed extent; each row is as tall as its tallest child) ---

        [Test]
        public void Measured_Vertical_ExactPositionsWhenFullyBuilt()
        {
            // Row heights differ: row0 = max(10,20)=20, row1 = max(30,40)=40, row2 = max(50,60)=60.
            var sizes = new[] { 10f, 20f, 30f, 40f, 50f, 60f };
            var state = new FakeSliverGridState
            {
                GridDelegate = new SliverGridDelegateWithFixedCrossAxisCount(2), // no main extent => measured
                ItemCount = sizes.Length,
                VirtualizationCacheExtent = 10000, // build & measure the whole grid in one pass
                BuildWindow = (start, end) =>
                    Enumerable.Range(start, end - start).Select(i => VCell(sizes[i])).ToArray(),
            };

            var render = new RenderSliverGrid(state);
            render.Layout(new LayoutConstraints(0, 0, 200, 50));

            Assert.AreEqual(new Vector2(0, 0), PositionOf(state, 0));
            Assert.AreEqual(new Vector2(100, 0), PositionOf(state, 1));
            Assert.AreEqual(new Vector2(0, 20), PositionOf(state, 2)); // row 1 starts after row 0's height (20)
            Assert.AreEqual(new Vector2(0, 60), PositionOf(state, 4)); // row 2 starts after 20 + 40
            Assert.AreEqual(new Vector2(100, 40), SizeOf(state, 3)); // cell keeps its own measured height (40)

            // rows: 20 + 40 + 60 = 120.
            Assert.AreEqual(120f, render.TotalContentSize(), 0.01f);
        }

        [Test]
        public void Measured_Vertical_LastRowTrailingEdge_CoincidesWithContentSize()
        {
            // The grid analog of the list's anti-drift/anti-clip regression: a tall header row seeds the row
            // estimate well above the rest, but TotalContentSize() and the positioned last row must stay in
            // lockstep (both routed through EstimateRowLeadingEdgeOffset) so the final row isn't pushed past
            // the content rect and clipped by the mask.
            float SizeOf(int i) => i == 0 ? 300f : 50f; // item 0 makes row 0 a 300px header
            var state = new FakeSliverGridState
            {
                GridDelegate = new SliverGridDelegateWithFixedCrossAxisCount(2),
                ItemCount = 50,
                VirtualizationCacheExtent = 0,
                BuildWindow = (start, end) =>
                    Enumerable.Range(start, end - start).Select(i => VCell(SizeOf(i))).ToArray(),
            };

            var render = new RenderSliverGrid(state);
            var viewport = new LayoutConstraints(0, 0, 200, 200);

            state.ScrollPixelOffset = 0f;
            render.Layout(viewport);

            for (var pass = 0; pass < 80; pass++)
            {
                state.ScrollPixelOffset = Mathf.Max(0f, render.TotalContentSize() - 200f);
                render.Layout(viewport);
            }

            var visible = state.LastVisibleChildren;
            Assert.IsNotEmpty(
                visible,
                "the bottom of the grid must have visible cells once the estimate settles"
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
                "the final row's trailing edge must coincide with the content size the view is sized to"
            );
        }

        // --- Scroll-to ---

        [Test]
        public void CalculateScrollPixelOffset_Fixed_StartCenterEnd()
        {
            var state = new FakeSliverGridState
            {
                GridDelegate = new SliverGridDelegateWithFixedCrossAxisCount(
                    2,
                    mainAxisExtent: 50f
                ),
                AllChildren = Enumerable.Range(0, 10).Select(_ => VCell(0)).ToArray(),
            };

            var render = new RenderSliverGrid(state);
            render.Layout(new LayoutConstraints(0, 0, 200, 100)); // viewport main = 100

            // item 4 -> row 2 -> leading edge 100. content = 5 rows * 50 = 250, scrollable = 150.
            Assert.AreEqual(
                100f,
                render.CalculateScrollPixelOffset(4, ScrollToPosition.Start),
                0.01f
            );
            Assert.AreEqual(
                75f,
                render.CalculateScrollPixelOffset(4, ScrollToPosition.Center),
                0.01f
            );
            Assert.AreEqual(50f, render.CalculateScrollPixelOffset(4, ScrollToPosition.End), 0.01f);
        }

        [Test]
        public void CalculateScrollPixelOffset_Measured_UsesExactRowPrefix()
        {
            // Whole grid built in one pass (huge cache) so every row extent is exact and deterministic.
            var sizes = new[] { 10f, 20f, 30f, 40f, 50f, 60f };
            var state = new FakeSliverGridState
            {
                GridDelegate = new SliverGridDelegateWithFixedCrossAxisCount(2),
                ItemCount = sizes.Length,
                VirtualizationCacheExtent = 10000,
                BuildWindow = (start, end) =>
                    Enumerable.Range(start, end - start).Select(i => VCell(sizes[i])).ToArray(),
            };

            var render = new RenderSliverGrid(state);
            render.Layout(new LayoutConstraints(0, 0, 200, 40)); // viewport main = 40

            // row 2 (items 4,5) leading edge = 20 + 40 = 60; row 2 height = 60. content = 120, scrollable = 80.
            Assert.AreEqual(
                60f,
                render.CalculateScrollPixelOffset(4, ScrollToPosition.Start),
                0.01f
            );
            Assert.AreEqual(
                70f,
                render.CalculateScrollPixelOffset(4, ScrollToPosition.Center),
                0.01f
            ); // 60 - (40-60)/2 = 70
            Assert.AreEqual(80f, render.CalculateScrollPixelOffset(4, ScrollToPosition.End), 0.01f); // 60 - (40-60) = 80
        }
    }
}
