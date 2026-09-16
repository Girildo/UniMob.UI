using System.Collections;
using System.Linq;
using NUnit.Framework;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     A list scrolled far down whose source then shrinks to a fraction of its length must land on
    ///     an offset the shorter content has, and show every item inside the viewport there.
    /// </summary>
    /// <remarks>
    ///     Collapsing every group of a long tree does exactly this. The controller still holds the old
    ///     offset, the render object builds its window for it, and a ScrollRect clamped onto the short
    ///     content shows a region that window does not cover: rows go missing until the next scroll.
    /// </remarks>
    public class ScrollListShrinkViewTests
    {
        private const float Viewport = 700f;
        private const float ItemHeight = 100f;
        private const int LongCount = 60;
        private const float ScrolledOffset = 3000f;

        private LifetimeController lifetime = null!;
        private GameObject canvasGo = null!;

        [SetUp]
        public void SetUp()
        {
            this.lifetime = new LifetimeController();

            this.canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            var canvas = this.canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        [TearDown]
        public void TearDown()
        {
            this.lifetime.Dispose();
            Object.DestroyImmediate(this.canvasGo);
        }

        [UnityTest]
        public IEnumerator ScrolledByTheUser_ShrinkingBelowTheViewport_ShowsEveryItemFromTheTop()
        {
            yield return this.ScrollThenShrink(shrunkCount: 4, scrollByUser: true);
        }

        [UnityTest]
        public IEnumerator ScrolledByTheUser_ShrinkingToJustOverTheViewport_ShowsTheEndOfTheList()
        {
            yield return this.ScrollThenShrink(shrunkCount: 9, scrollByUser: true);
        }

        [UnityTest]
        public IEnumerator ScrolledByTheController_ShrinkingBelowTheViewport_ShowsEveryItemFromTheTop()
        {
            yield return this.ScrollThenShrink(shrunkCount: 4, scrollByUser: false);
        }

        [UnityTest]
        public IEnumerator ScrolledByTheController_ShrinkingToJustOverTheViewport_ShowsTheEndOfTheList()
        {
            yield return this.ScrollThenShrink(shrunkCount: 9, scrollByUser: false);
        }

        [UnityTest]
        public IEnumerator GrowingAgainAfterTheShrink_StaysWhereTheShrinkLeftTheList()
        {
            var controller = new ScrollController(this.lifetime.Lifetime);
            var count = Atom.Value(LongCount);
            yield return this.ScrollThenShrink(
                shrunkCount: 4,
                scrollByUser: true,
                controller,
                count
            );

            count.Value = LongCount;
            yield return Settle();

            Assert.AreEqual(
                0f,
                controller.PixelOffset,
                1f,
                "the shrink moved the list to the top; growing it again must not jump back to the "
                    + "offset the long list had before"
            );
        }

        [UnityTest]
        public IEnumerator AGrid_ShrinkingBelowTheViewport_ShowsEveryItemFromTheTop()
        {
            yield return this.ScrollThenShrink(shrunkCount: 4, scrollByUser: true, asGrid: true);
        }

        [UnityTest]
        public IEnumerator AGrid_ShrinkingToJustOverTheViewport_ShowsTheEndOfTheList()
        {
            yield return this.ScrollThenShrink(shrunkCount: 9, scrollByUser: true, asGrid: true);
        }

        private IEnumerator ScrollThenShrink(
            int shrunkCount,
            bool scrollByUser,
            bool asGrid = false
        ) =>
            this.ScrollThenShrink(
                shrunkCount,
                scrollByUser,
                new ScrollController(this.lifetime.Lifetime),
                Atom.Value(LongCount),
                asGrid
            );

        private IEnumerator ScrollThenShrink(
            int shrunkCount,
            bool scrollByUser,
            ScrollController controller,
            MutableAtom<int> count,
            bool asGrid = false
        )
        {
            this.Host(controller, count, asGrid);
            yield return Settle();

            var scrollRect = this.canvasGo.GetComponentInChildren<ScrollRect>();
            Assert.IsNotNull(scrollRect, "the list never produced a ScrollRect");

            if (scrollByUser)
            {
                // What a drag ends in: the ScrollRect moves, and the view reports the move.
                var range = LongCount * ItemHeight - Viewport;
                scrollRect.verticalNormalizedPosition = 1f - ScrolledOffset / range;
            }
            else
            {
                controller.ScrollTo(Key.Of(30), 0.2f, ScrollToPosition.Start);
                yield return new WaitForSecondsRealtime(0.2f);
            }

            yield return Settle();
            Assume.That(
                controller.PixelOffset,
                Is.EqualTo(ScrolledOffset).Within(1f),
                "the list should be scrolled far down before it shrinks"
            );

            count.Value = shrunkCount;

            // Every frame, not only the settled one: a single frame drawn for an offset the content
            // no longer has is the list flashing empty. Each frame is held to the list the content
            // currently stands for, since the shrink reaches the view a frame after the source.
            for (var frame = 1; frame <= SettleFrames; frame++)
            {
                yield return null;

                var shownNow = ShownPixelOffset(scrollRect);
                var countNow = Mathf.RoundToInt(scrollRect.content.rect.height / ItemHeight);
                var expectedNow = VisibleIndices(shownNow, countNow);
                var renderedNow = RenderedItemCount(scrollRect);
                Assert.AreEqual(
                    expectedNow,
                    renderedNow,
                    $"frame {frame} after the shrink, {countNow} items long: {expectedNow} items intersect the viewport at "
                        + $"{shownNow}px, but {renderedNow} item views are active there "
                        + $"(controller at {controller.PixelOffset}px). Content children: "
                        + DescribeChildren(scrollRect)
                );
            }

            var maxOffset = Mathf.Max(0f, shrunkCount * ItemHeight - Viewport);
            var shown = ShownPixelOffset(scrollRect);
            var expectedVisible = VisibleIndices(controller.PixelOffset, shrunkCount);
            var rendered = RenderedItemCount(scrollRect);

            Assert.AreEqual(
                maxOffset,
                controller.PixelOffset,
                1f,
                $"the content is now {shrunkCount * ItemHeight}px, so no offset past {maxOffset}px "
                    + "exists; the controller still holding it builds the window for nothing"
            );
            Assert.AreEqual(
                controller.PixelOffset,
                shown,
                1f,
                $"the ScrollRect shows {shown}px while the window was built for "
                    + $"{controller.PixelOffset}px"
            );
            Assert.AreEqual(
                expectedVisible,
                rendered,
                $"{expectedVisible} items intersect the viewport at {controller.PixelOffset}px, but "
                    + $"{rendered} item views are active there. Content children: "
                    + DescribeChildren(scrollRect)
            );
        }

        private static int VisibleIndices(float offset, int count) =>
            Enumerable
                .Range(0, count)
                .Count(index =>
                    index * ItemHeight < offset + Viewport && (index + 1) * ItemHeight > offset
                );

        private static int RenderedItemCount(ScrollRect scrollRect)
        {
            var content = scrollRect.content;
            var shown = ShownPixelOffset(scrollRect);

            var rendered = 0;
            for (var i = 0; i < content.childCount; i++)
            {
                var child = (RectTransform)content.GetChild(i);
                if (!child.gameObject.activeInHierarchy)
                    continue;

                // Top edge in content space, measured downwards from the content's top.
                var top = -(child.anchoredPosition.y + child.rect.height * (1f - child.pivot.y));
                var bottom = top + child.rect.height;
                if (top < shown + Viewport && bottom > shown)
                    rendered++;
            }

            return rendered;
        }

        private static string DescribeChildren(ScrollRect scrollRect)
        {
            var content = scrollRect.content;
            var children = Enumerable
                .Range(0, content.childCount)
                .Select(i => (RectTransform)content.GetChild(i))
                .Select(child =>
                    $"[{child.name} active={child.gameObject.activeInHierarchy} "
                    + $"pos={child.anchoredPosition} size={child.rect.size}]"
                );
            return $"content height {content.rect.height}, " + string.Join(" ", children);
        }

        private static float ShownPixelOffset(ScrollRect scrollRect)
        {
            var range = scrollRect.content.rect.height - scrollRect.viewport.rect.height;
            return (1f - scrollRect.verticalNormalizedPosition) * Mathf.Max(0f, range);
        }

        /// <param name="asGrid">
        ///     Hosts a one-column grid with a fixed row extent instead, whose geometry is the list's.
        /// </param>
        private void Host(ScrollController controller, MutableAtom<int> count, bool asGrid)
        {
            var panelGo = new GameObject("ViewPanel", typeof(RectTransform));
            panelGo.transform.SetParent(this.canvasGo.transform, false);

            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.sizeDelta = Vector2.zero;

            var panel = panelGo.AddComponent<ViewPanel>();

            UniMobUI.RunApp(
                this.lifetime.Lifetime,
                panel,
                _ => new Align
                {
                    Alignment = Alignment.TopLeft,
                    Child = new SizedBox
                    {
                        Width = 800,
                        Height = Viewport,
                        Child = asGrid
                            ? new ScrollGrid
                            {
                                ScrollController = controller,
                                MovementType = MovementType.Clamped,
                                CrossAxisCount = 1,
                                MainAxisExtent = ItemHeight,
                                ItemCount = count.Value,
                                KeyToIndexResolver = key => IndexOf(key, count.Value),
                                ItemBuilder = Item,
                            }
                            : new ScrollList
                            {
                                ScrollController = controller,
                                MovementType = MovementType.Clamped,
                                ItemCount = count.Value,
                                KeyToIndexResolver = key => IndexOf(key, count.Value),
                                ItemBuilder = Item,
                            },
                    },
                }
            );
        }

        private static Widget Item(BuildContext context, int index) =>
            new Container(height: ItemHeight)
            {
                Key = Key.Of(index),
                BackgroundColor = Color.white,
            };

        private static int? IndexOf(Key key, int count)
        {
            for (var index = 0; index < count; index++)
            {
                if (Key.Of(index).Equals(key))
                    return index;
            }

            return null;
        }

        private const int SettleFrames = 6;

        private static IEnumerator Settle()
        {
            for (var i = 0; i < SettleFrames; i++)
            {
                yield return null;
            }
        }
    }
}
