using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    // Hostile inputs against the invariant of a lazy scrollable: after a layout pass the placed
    // children cover the part of the viewport the content reaches, consecutive slots touch, every
    // placed child is the item it claims to be, and the State exposes exactly the placed children.
    // A slot is an item of the list and a row of the grid.
    public class ScrollableCoverageAdversarialTests
    {
        private const float Cross = 800f;
        private const float DefaultViewport = 700f;

        private sealed class Spec
        {
            public string Kind = "List";
            public Axis Axis = Axis.Vertical;
            public int ItemCount;
            public float Spacing;
            public float? Cache;
            public float? ItemExtent;
            public int Columns = 1;
            public float? MaxCrossAxisExtent;
            public RectPadding Padding;
            public Func<int, float> ExtentOf = _ => 50f;

            public Spec With(Action<Spec> change)
            {
                var copy = (Spec)MemberwiseClone();
                change(copy);
                return copy;
            }
        }

        private sealed class Rig
        {
            public Spec Spec;
            public State State;
            public ScrollController Controller;
            public float Viewport = DefaultViewport;
            public float CrossSize = Cross;
            public int BuilderCalls;
            public readonly HashSet<int> BuiltIndices = new();

            public bool IsHorizontal => Spec.Axis == Axis.Horizontal;

            public float CrossPadding =>
                IsHorizontal ? Spec.Padding.Vertical : Spec.Padding.Horizontal;

            public float MainStartPadding => IsHorizontal ? Spec.Padding.Left : Spec.Padding.Top;

            public float MainEndPadding => IsHorizontal ? Spec.Padding.Right : Spec.Padding.Bottom;

            public int Columns =>
                Spec.Kind == "List" ? 1
                : Spec.MaxCrossAxisExtent.HasValue
                    ? Mathf.Max(
                        1,
                        Mathf.CeilToInt((CrossSize - CrossPadding) / Spec.MaxCrossAxisExtent.Value)
                    )
                : Spec.Columns;

            public int SlotCount => (Spec.ItemCount + Columns - 1) / Columns;

            public float CacheExtent => Spec.Cache ?? Viewport;

            public float SlotExtent(int slot)
            {
                if (Spec.ItemExtent.HasValue)
                    return Spec.ItemExtent.Value;

                var extent = 0f;
                var first = slot * Columns;
                for (var i = first; i < Mathf.Min(first + Columns, Spec.ItemCount); i++)
                    extent = Mathf.Max(extent, Spec.ExtentOf(i));
                return extent;
            }

            // The builder models the usual `items[index]` body: an index the data no longer holds
            // throws. The index rides on the cross axis of the box, which the scrollable overrides
            // with a tight constraint, so a placed child can be asked which item it is.
            private Widget BuildItem(BuildContext context, int index)
            {
                BuilderCalls++;
                BuiltIndices.Add(index);

                if (index < 0 || index >= Spec.ItemCount)
                    throw new IndexOutOfRangeException(
                        $"ItemBuilder asked for index {index} of {Spec.ItemCount} items"
                    );

                var extent = Spec.ExtentOf(index);
                return new FixedSizeBox
                {
                    Key = Key.Of(index),
                    Size = IsHorizontal ? new Vector2(extent, index) : new Vector2(index, extent),
                };
            }

            public Widget MakeWidget() =>
                Spec.Kind == "List"
                    ? new ScrollList
                    {
                        ScrollController = Controller,
                        Axis = Spec.Axis,
                        Spacing = Spec.Spacing,
                        Padding = Spec.Padding,
                        ItemCount = Spec.ItemCount,
                        ItemExtent = Spec.ItemExtent,
                        VirtualizationCacheExtent = Spec.Cache,
                        ItemBuilder = BuildItem,
                    }
                    : new ScrollGrid
                    {
                        ScrollController = Controller,
                        Axis = Spec.Axis,
                        CrossAxisCount = Spec.MaxCrossAxisExtent.HasValue ? null : Spec.Columns,
                        MaxCrossAxisExtent = Spec.MaxCrossAxisExtent,
                        MainAxisExtent = Spec.ItemExtent,
                        MainAxisSpacing = Spec.Spacing,
                        Padding = Spec.Padding,
                        ItemCount = Spec.ItemCount,
                        VirtualizationCacheExtent = Spec.Cache,
                        ItemBuilder = BuildItem,
                    };

            public static Rig Mount(Spec spec)
            {
                var rig = new Rig
                {
                    Spec = spec,
                    Controller = new ScrollController(new LifetimeController().Lifetime),
                };
                rig.State = TestHarness.Mount(rig.MakeWidget());
                return rig;
            }

            public void Apply(Spec spec)
            {
                Spec = spec;
                State = TestHarness.Update(State, MakeWidget());
            }

            public void Layout(float offset)
            {
                Controller.PixelOffset = offset;
                BuilderCalls = 0;
                BuiltIndices.Clear();
                TestHarness.Layout(
                    State,
                    IsHorizontal
                        ? LayoutConstraints.Tight(Viewport, CrossSize)
                        : LayoutConstraints.Tight(CrossSize, Viewport)
                );
            }

            public void LayoutAndAssert(float offset, string step, float tolerance = 0.5f)
            {
                Layout(offset);
                AssertInvariant(this, offset, step, tolerance);
            }
        }

        private readonly struct Placed
        {
            public Placed(int index, float start, float extent)
            {
                Index = index;
                Start = start;
                Extent = extent;
            }

            public int Index { get; }
            public float Start { get; }
            public float Extent { get; }
            public float End => Start + Extent;
        }

        private static void AssertInvariant(
            Rig rig,
            float requestedOffset,
            string step,
            float tolerance
        )
        {
            var spec = rig.Spec;
            var horizontal = rig.IsHorizontal;
            float Main(Vector2 v) => horizontal ? v.x : v.y;
            float CrossOf(Vector2 v) => horizontal ? v.y : v.x;

            var renderObject = (IScrollableRenderObject)rig.State.RenderObject;
            var layout = renderObject.ChildrenLayout;
            var children = ((IMultiChildLayoutState)rig.State).Children;
            var contentSize = renderObject.TotalContentSize();

            var offset = Mathf.Min(requestedOffset, Mathf.Max(0f, contentSize - rig.Viewport));
            var contentStart = rig.MainStartPadding;
            var contentEnd = contentSize - rig.MainEndPadding;
            var low = Mathf.Max(offset, contentStart);
            var high = Mathf.Min(offset + rig.Viewport, contentEnd);
            var where =
                $"{step}: {spec.Kind} {spec.Axis}, {spec.ItemCount} items, viewport "
                + $"[{offset}, {offset + rig.Viewport}) of {contentSize}px of content";

            Assert.AreEqual(
                layout.Count,
                children.Length,
                $"{where}: State exposes {children.Length} children for {layout.Count} layouts"
            );

            var placed = new List<Placed>();
            for (var i = 0; i < children.Length; i++)
            {
                Assert.IsNotNull(children[i], $"{where}: State child {i} is null");
                var box = (FixedSizeBox)children[i].RawWidget;
                var index = Mathf.RoundToInt(CrossOf(box.Size));
                Assert.Less(index, spec.ItemCount, $"{where}: placed index {index}");

                var extent = Main(layout[i].Size);
                Assert.AreEqual(
                    spec.ItemExtent ?? spec.ExtentOf(index),
                    extent,
                    0.01f,
                    $"{where}: item {index} is laid out with another item's extent"
                );
                placed.Add(new Placed(index, Main(layout[i].Position), extent));
            }

            // Slots in order, each where the previous one ends.
            var slots = placed
                .GroupBy(p => p.Index / rig.Columns)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var starts = g.Select(p => p.Start).ToArray();
                    Assert.AreEqual(
                        starts.Min(),
                        starts.Max(),
                        0.001f,
                        $"{where}: cells of row {g.Key} start at different offsets"
                    );
                    return (slot: g.Key, start: starts.Min(), end: g.Max(p => p.End));
                })
                .ToList();

            for (var i = 1; i < slots.Count; i++)
            {
                var previous = slots[i - 1];
                var current = slots[i];
                Assert.AreEqual(
                    previous.slot + 1,
                    current.slot,
                    $"{where}: slot {previous.slot} is followed by slot {current.slot}"
                );
                Assert.AreEqual(
                    previous.start + rig.SlotExtent(previous.slot) + spec.Spacing,
                    current.start,
                    tolerance,
                    $"{where}: slot {current.slot} does not start where slot {previous.slot} ends"
                );
            }

            if (spec.ItemExtent.HasValue)
            {
                foreach (var slot in slots)
                    Assert.AreEqual(
                        contentStart + slot.slot * (spec.ItemExtent.Value + spec.Spacing),
                        slot.start,
                        tolerance,
                        $"{where}: fixed-extent slot {slot.slot} is misplaced"
                    );
            }

            if (high - low <= tolerance)
                return;

            Assert.IsNotEmpty(slots, $"{where}: nothing placed over [{low}, {high})");

            var firstStart = slots[0].start;
            var lastEnd = slots.Max(s => s.end);
            var span = $"placed {placed.Count} children over [{firstStart}, {lastEnd})";

            // A viewport edge may fall in the gap between two slots.
            Assert.LessOrEqual(
                firstStart,
                low + spec.Spacing + tolerance,
                $"{where}: gap of {firstStart - low}px at the leading edge; {span}"
            );
            Assert.GreaterOrEqual(
                lastEnd,
                high - spec.Spacing - tolerance,
                $"{where}: gap of {high - lastEnd}px at the trailing edge; {span}"
            );

            // With a cache extent no culling happens at the content edges, so they must be exact:
            // a last slot ending past the content is clipped, one ending short leaves a hole.
            if (rig.CacheExtent > 0f && offset + rig.Viewport >= contentEnd - tolerance)
            {
                Assert.AreEqual(rig.SlotCount - 1, slots[^1].slot, $"{where}: last slot missing");
                Assert.AreEqual(
                    contentEnd,
                    slots[^1].start + rig.SlotExtent(slots[^1].slot),
                    tolerance,
                    $"{where}: the last slot does not end where the content ends; {span}"
                );
            }

            if (rig.CacheExtent > 0f && offset <= contentStart)
            {
                Assert.AreEqual(
                    contentStart,
                    firstStart,
                    tolerance,
                    $"{where}: the first slot does not start where the content starts; {span}"
                );
            }
        }

        private static float[] RandomExtents(int seed, int count, float tall)
        {
            var random = new System.Random(seed);
            var extents = new float[count];
            for (var i = 0; i < count; i++)
            {
                var roll = random.NextDouble();
                extents[i] =
                    roll < 0.2 ? 0f
                    : roll < 0.23 ? tall
                    : 10f + (float)random.NextDouble() * 190f;
            }

            return extents;
        }

        private static IEnumerable<TestCaseData> RandomWalkCases() =>
            from kind in new[] { "List", "Grid" }
            from axis in new[] { Axis.Vertical, Axis.Horizontal }
            from shape in new (float spacing, float? cache)[]
            {
                (0f, null),
                (8f, null),
                (8f, 0f),
                (0f, 0f),
                (8f, 5000f),
            }
            from seed in new[] { 1, 2, 3 }
            select new TestCaseData(kind, axis, shape.spacing, shape.cache, seed);

        [TestCaseSource(nameof(RandomWalkCases))]
        public void RandomExtents_RandomWalk_KeepsTheInvariant(
            string kind,
            Axis axis,
            float spacing,
            float? cache,
            int seed
        )
        {
            // Taller than the viewport plus both cache extents at their default size.
            var extents = RandomExtents(seed, 1201, tall: DefaultViewport * 3f + 500f);
            var rig = Rig.Mount(
                new Spec
                {
                    Kind = kind,
                    Axis = axis,
                    Spacing = spacing,
                    Cache = cache,
                    Columns = 3,
                    Padding = RectPadding.FromLTRB(10, 30, 20, 40),
                    // Not a multiple of the column count: the last row is partial.
                    ItemCount = kind == "Grid" ? 1201 : 400,
                    ExtentOf = i => extents[i],
                }
            );

            var random = new System.Random(seed * 7919);
            var offset = 0f;
            rig.LayoutAndAssert(offset, "initial layout");

            for (var step = 0; step < 150; step++)
            {
                var content = ((IScrollableRenderObject)rig.State.RenderObject).TotalContentSize();
                var roll = random.NextDouble();
                offset =
                    roll < 0.55 ? offset + (float)(random.NextDouble() * 600 - 250)
                    : roll < 0.80 ? (float)random.NextDouble() * content
                    : roll < 0.85 ? -(float)random.NextDouble() * 400f
                    : roll < 0.92 ? content + 1000f
                    : Mathf.Max(0f, content - rig.Viewport);

                rig.LayoutAndAssert(offset, $"step {step} to {offset}");
            }
        }

        [TestCase("List")]
        [TestCase("Grid")]
        public void ItemCountShrinkingAndGrowingUnderADeepOffset_KeepsTheInvariant(string kind)
        {
            var extents = RandomExtents(11, 3000, tall: 2600f);
            var spec = new Spec
            {
                Kind = kind,
                Spacing = 6f,
                Columns = 3,
                ItemCount = 900,
                ExtentOf = i => extents[i],
            };
            var rig = Rig.Mount(spec);

            var deep = 0f;
            for (var step = 0; step < 40; step++)
            {
                deep = step * 450f;
                rig.LayoutAndAssert(deep, $"scrolling down to {deep}");
            }

            foreach (var count in new[] { 120, 3000, 7, 1, 0, 900, 2, 3000 })
            {
                rig.Apply(spec.With(s => s.ItemCount = count));
                rig.LayoutAndAssert(deep, $"count {count}, offset kept at {deep}");
                rig.LayoutAndAssert(deep, $"count {count}, second pass at {deep}");
                rig.LayoutAndAssert(deep - 300f, $"count {count}, scrolled up from {deep}");
                rig.LayoutAndAssert(deep + 300f, $"count {count}, scrolled down from {deep}");
            }
        }

        [Test]
        public void GridColumnCountChangingUnderADeepOffset_KeepsTheInvariant()
        {
            // Row r holds other items after the change, so every row measurement taken is stale.
            var extents = RandomExtents(5, 1801, tall: 2600f);
            var spec = new Spec
            {
                Kind = "Grid",
                Spacing = 4f,
                Columns = 3,
                Padding = RectPadding.FromLTRB(0, 25, 0, 35),
                ItemCount = 1801,
                ExtentOf = i => extents[i],
            };
            var rig = Rig.Mount(spec);

            var offset = 0f;
            for (var step = 0; step < 30; step++)
            {
                offset = step * 500f;
                rig.LayoutAndAssert(offset, $"scrolling down to {offset}");
            }

            foreach (var columns in new[] { 5, 1, 4, 2, 7, 3 })
            {
                rig.Apply(spec.With(s => s.Columns = columns));
                rig.LayoutAndAssert(offset, $"{columns} columns at {offset}");
                rig.LayoutAndAssert(offset + 350f, $"{columns} columns, scrolled down");
                rig.LayoutAndAssert(offset - 900f, $"{columns} columns, scrolled up");
            }
        }

        [TestCase("List")]
        [TestCase("Grid")]
        public void ViewportResizingBetweenPasses_KeepsTheInvariant(string kind)
        {
            var extents = RandomExtents(21, 1500, tall: 2600f);
            var rig = Rig.Mount(
                new Spec
                {
                    Kind = kind,
                    Spacing = 5f,
                    // The column count follows the cross size: 800 / 110 -> 8, 330 -> 3, 1000 -> 10.
                    MaxCrossAxisExtent = 110f,
                    ItemCount = 1500,
                    ExtentOf = i => extents[i],
                }
            );

            rig.LayoutAndAssert(0f, "initial layout");
            rig.LayoutAndAssert(6000f, "jump to 6000");

            var shapes = new (float viewport, float cross)[]
            {
                (200f, 800f),
                (1500f, 330f),
                (0f, 800f),
                (50f, 1000f),
                (3000f, 800f),
                (700f, 800f),
            };
            foreach (var (viewport, cross) in shapes)
            {
                rig.Viewport = viewport;
                rig.CrossSize = cross;
                rig.LayoutAndAssert(6000f, $"viewport {viewport}x{cross} at 6000");
                rig.LayoutAndAssert(6400f, $"viewport {viewport}x{cross} at 6400");
                rig.LayoutAndAssert(1e7f, $"viewport {viewport}x{cross} past the end");
            }
        }

        [TestCase("List", 10f)]
        [TestCase("List", 5000f)]
        [TestCase("List", 0f)]
        [TestCase("Grid", 10f)]
        [TestCase("Grid", 5000f)]
        [TestCase("Grid", 0f)]
        public void ASingleItem_KeepsTheInvariant(string kind, float extent)
        {
            var rig = Rig.Mount(
                new Spec
                {
                    Kind = kind,
                    Spacing = 8f,
                    Columns = 3,
                    ItemCount = 1,
                    ExtentOf = _ => extent,
                }
            );

            foreach (var offset in new[] { 0f, 100f, -100f, 4300f, 4299.5f, 1e6f, 0f })
                rig.LayoutAndAssert(offset, $"offset {offset}");
        }

        [TestCase("List", Axis.Vertical)]
        [TestCase("List", Axis.Horizontal)]
        [TestCase("Grid", Axis.Vertical)]
        [TestCase("Grid", Axis.Horizontal)]
        public void FixedItemExtent_IsExactAtEveryOffset_AndBuildsOnlyTheWindow(
            string kind,
            Axis axis
        )
        {
            var rig = Rig.Mount(
                new Spec
                {
                    Kind = kind,
                    Axis = axis,
                    Spacing = 3f,
                    Columns = 4,
                    ItemExtent = 47f,
                    ItemCount = 100_003,
                    // Ignored: the extent is imposed by a tight constraint.
                    ExtentOf = i => (i * 37) % 400,
                }
            );

            // 100k slots of 50px: up to 5M px for the list, where a float resolves 0.5px.
            var content = rig.SlotCount * 50f - 3f;
            var offsets = new[]
            {
                0f,
                49f,
                50f,
                12_345.6f,
                content * 0.5f,
                content - rig.Viewport,
                content + 5000f,
                -250f,
                content * 0.25f,
            };
            foreach (var offset in offsets)
            {
                rig.LayoutAndAssert(offset, $"offset {offset}", tolerance: 1f);

                // viewport + 2 cache = 2100px = 42 slots of 50px, plus the two partial ones.
                Assert.LessOrEqual(
                    rig.BuiltIndices.Count,
                    44 * rig.Columns,
                    $"offset {offset}: a fixed-extent window needs no widening"
                );
            }
        }

        private static IEnumerable<TestCaseData> ExtremeCacheCases() =>
            from kind in new[] { "List", "Grid" }
            from cache in new[] { 0f, 1e12f, float.MaxValue, float.PositiveInfinity }
            from fixedExtent in new[] { false, true }
            select new TestCaseData(kind, cache, fixedExtent);

        [TestCaseSource(nameof(ExtremeCacheCases))]
        public void ExtremeCacheExtents_KeepTheInvariant(string kind, float cache, bool fixedExtent)
        {
            var extents = RandomExtents(31, 300, tall: 2600f);
            var rig = Rig.Mount(
                new Spec
                {
                    Kind = kind,
                    Spacing = 8f,
                    Columns = 2,
                    Cache = cache,
                    ItemExtent = fixedExtent ? 60f : null,
                    ItemCount = 300,
                    ExtentOf = i => extents[i],
                }
            );

            foreach (var offset in new[] { 0f, 5000f, 333f, 1e6f, -50f, 9000f })
            {
                rig.LayoutAndAssert(offset, $"cache {cache}, offset {offset}");

                // One build of each item is all an unbounded cache can ask for.
                Assert.LessOrEqual(
                    rig.BuilderCalls,
                    2 * 300,
                    $"cache {cache}, offset {offset}: ItemBuilder ran {rig.BuilderCalls} times"
                );
            }
        }

        [Test]
        public void HundredThousandFractionalItems_StayExactAtTheFarEnd()
        {
            // 100k items of 33.3 / 43.3px: about 3.8M px of content, where a float resolves 0.25px
            // up to 4M. A window of ~60 items is placed by accumulating float positions.
            var rig = Rig.Mount(
                new Spec { ItemCount = 100_000, ExtentOf = i => 33.3f + (i % 2) * 10f }
            );

            rig.LayoutAndAssert(0f, "initial layout", tolerance: 1f);

            var content = ((IScrollableRenderObject)rig.State.RenderObject).TotalContentSize();
            foreach (var fraction in new[] { 0.5f, 0.9f, 0.999f, 1f, 2f })
            {
                rig.LayoutAndAssert(content * fraction, $"jump to {fraction}", tolerance: 1f);
                content = ((IScrollableRenderObject)rig.State.RenderObject).TotalContentSize();
            }

            // Walk up from the very end, where every rounding error of the window accumulates.
            var end = content - rig.Viewport;
            for (var step = 0; step < 40; step++)
                rig.LayoutAndAssert(end - step * 333f, $"walking up, step {step}", tolerance: 1f);
        }

        [TestCase(100_000, 50_000)]
        [TestCase(2_000, 1_000)]
        public void JumpingFarIntoUnmeasuredItemsOfAnotherSize_BuildsAboutOneWindow(
            int itemCount,
            int targetIndex
        )
        {
            // The first screen is 100px items, the rest are 50px. A scrollbar drag lands on the
            // estimated position of targetIndex.
            var rig = Rig.Mount(
                new Spec { ItemCount = itemCount, ExtentOf = i => i < 15 ? 100f : 50f }
            );
            rig.LayoutAndAssert(0f, "initial layout");

            var watch = Stopwatch.StartNew();
            rig.LayoutAndAssert(targetIndex * 100f, $"jump to item {targetIndex}");
            watch.Stop();

            // viewport + 2 cache = 2100px = 42 items of 50px. Four times that is generous.
            var needed = Mathf.CeilToInt(3f * DefaultViewport / 50f);
            Assert.LessOrEqual(
                rig.BuiltIndices.Count,
                4 * needed,
                $"one pass built {rig.BuiltIndices.Count} distinct items with {rig.BuilderCalls} "
                    + $"ItemBuilder calls in {watch.ElapsedMilliseconds}ms; {needed} cover the range"
            );
        }

        [Test]
        public void JumpingFarIntoTallerUnmeasuredItems_BuildsAboutOneWindow()
        {
            var rig = Rig.Mount(
                new Spec { ItemCount = 100_000, ExtentOf = i => i < 40 ? 50f : 100f }
            );
            rig.LayoutAndAssert(0f, "initial layout");

            var watch = Stopwatch.StartNew();
            rig.LayoutAndAssert(50_000 * 50f, "jump to the estimated item 50000");
            watch.Stop();

            // viewport + 2 cache = 2100px = 21 items of 100px.
            var needed = Mathf.CeilToInt(3f * DefaultViewport / 100f);
            Assert.LessOrEqual(
                rig.BuiltIndices.Count,
                4 * needed,
                $"one pass built {rig.BuiltIndices.Count} distinct items with {rig.BuilderCalls} "
                    + $"ItemBuilder calls in {watch.ElapsedMilliseconds}ms; {needed} cover the range"
            );
        }

        [Test]
        public void LeadingZeroExtentItems_DoNotInflateTheFirstWindow()
        {
            // 50 collapsed items, then 100px ones. The range needs the 50 plus 1400 / 100 items.
            var rig = Rig.Mount(
                new Spec { ItemCount = 100_000, ExtentOf = i => i < 50 ? 0f : 100f }
            );
            rig.LayoutAndAssert(0f, "initial layout");

            var needed = 50 + Mathf.CeilToInt(2f * DefaultViewport / 100f);
            Assert.LessOrEqual(
                rig.BuiltIndices.Count,
                4 * needed,
                $"the first pass built {rig.BuiltIndices.Count} distinct items with "
                    + $"{rig.BuilderCalls} ItemBuilder calls; {needed} cover the range"
            );
        }

        [TestCase("List", 20_000)]
        [TestCase("Grid", 20_000)]
        public void AllZeroExtents_BuildEveryItemOnceNotQuadratically(string kind, int itemCount)
        {
            // Every item sits at offset 0, inside the viewport, so all of them must be built. The
            // cost to watch is how often the builder runs for each.
            var rig = Rig.Mount(
                new Spec
                {
                    Kind = kind,
                    Columns = 2,
                    ItemCount = itemCount,
                    ExtentOf = _ => 0f,
                }
            );

            var watch = Stopwatch.StartNew();
            rig.Layout(0f);
            watch.Stop();

            Assert.AreEqual(itemCount, rig.BuiltIndices.Count, "every item is inside the viewport");
            Assert.LessOrEqual(
                rig.BuilderCalls,
                3 * itemCount,
                $"{itemCount} zero-extent items took {rig.BuilderCalls} ItemBuilder calls and "
                    + $"{watch.ElapsedMilliseconds}ms in one layout pass"
            );
        }

        [TestCase("List")]
        [TestCase("Grid")]
        public void ScrollingIntoUnmeasuredShortItems_RunsTheBuilderAboutOncePerWindowItem(
            string kind
        )
        {
            // The estimate (100px, from the first screen) is 2.5 times the items that follow, so
            // every pass reaches unmeasured items and has to widen.
            var rig = Rig.Mount(
                new Spec
                {
                    Kind = kind,
                    Columns = 2,
                    ItemCount = 20_000,
                    ExtentOf = i => i < 30 ? 100f : 40f,
                }
            );
            rig.LayoutAndAssert(0f, "initial layout");

            var worstRatio = 0f;
            var worstStep = "";
            for (var offset = 150f; offset < 30_000f; offset += 150f)
            {
                rig.LayoutAndAssert(offset, $"scrolling down to {offset}");
                var ratio = rig.BuilderCalls / (float)Mathf.Max(1, rig.BuiltIndices.Count);
                if (ratio > worstRatio)
                {
                    worstRatio = ratio;
                    worstStep =
                        $"offset {offset}: {rig.BuilderCalls} ItemBuilder calls for a window of "
                        + $"{rig.BuiltIndices.Count} items";
                }
            }

            // One selection plus one correction is the design; more is the window being rebuilt
            // from scratch on every widening round.
            Assert.LessOrEqual(worstRatio, 2f, worstStep);
        }
    }
}
