using System.Collections;
using System.IO;
using NUnit.Framework;
using UniMob.UI.Internal;
using UniMob.UI.Internal.Views;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Scrollbar = UniMob.UI.Widgets.Scrollbar;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     A mounted <see cref="Scrollbar" /> over a real list: the thumb's RectTransform, and the two
    ///     gestures the bar answers, driven through the pointer receivers the input module would call.
    /// </summary>
    /// <remarks>
    ///     Needs the real player loop, because everything asserted here is a Unity object a headless
    ///     mount never creates: the ScrollRect the list shows its offset through, and the RectTransform
    ///     the view stamps the thumb's geometry onto. The arithmetic behind both is pinned in EditMode.
    /// </remarks>
    public class ScrollbarViewTests
    {
        private const float Viewport = 700f;
        private const float ListWidth = 800f;
        private const float Thickness = 8f;
        private const float MinThumbExtent = 18f;
        private const float DragScreenPixels = 40f;
        private const int DragSteps = 3;

        private LifetimeController lifetime = null!;
        private GameObject canvasGo = null!;
        private GameObject? eventSystemGo;

        [SetUp]
        public void SetUp()
        {
            this.lifetime = new LifetimeController();

            this.canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            var canvas = this.canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            if (EventSystem.current == null)
            {
                this.eventSystemGo = new GameObject("EventSystem", typeof(EventSystem));
            }
        }

        [TearDown]
        public void TearDown()
        {
            this.lifetime.Dispose();
            Object.DestroyImmediate(this.canvasGo);

            if (this.eventSystemGo != null)
            {
                Object.DestroyImmediate(this.eventSystemGo);
                this.eventSystemGo = null;
            }
        }

        [UnityTest]
        public IEnumerator TheThumb_IsSizedAndPlacedFromTheListItShows()
        {
            var controller = new ScrollController(this.lifetime.Lifetime);

            this.Host(controller);
            yield return this.Settle();

            var scrollRect = this.canvasGo.GetComponentInChildren<ScrollRect>();
            Assert.IsNotNull(scrollRect, "the list never produced a ScrollRect");

            var thumb = ThumbRect(this.BarView());
            var content = scrollRect.content.rect.height;
            var proportional = Viewport * Viewport / content;
            var expected = Mathf.Max(proportional, MinThumbExtent);

            Assert.AreEqual(
                expected,
                thumb.rect.height,
                1f,
                $"a {Viewport}px viewport onto {content}px of content is a thumb of {proportional}px, "
                    + $"floored at the {MinThumbExtent}px that keeps it grabbable"
            );
            Assert.AreEqual(
                0f,
                TopOf(thumb),
                1f,
                "an untouched list puts the thumb against the start of the track"
            );
            Assert.AreEqual(
                Thickness,
                ((RectTransform)this.BarView().transform).rect.width,
                0.5f,
                "the bar takes its own thickness across, not the width the Positioned would allow it"
            );

            controller.JumpTo(3000f);

            yield return null;
            yield return null;
            yield return null;

            Assert.Greater(
                TopOf(thumb),
                1f,
                $"the list jumped to 3000px but the thumb is still {TopOf(thumb)}px from the top of "
                    + "the track, so the bar is reporting a position the list is not at"
            );
        }

        [UnityTest]
        public IEnumerator DraggingTheThumb_ScrollsTheList()
        {
            var controller = new ScrollController(this.lifetime.Lifetime);

            this.Host(controller);
            yield return this.Settle();

            var scrollRect = this.canvasGo.GetComponentInChildren<ScrollRect>();
            Assert.IsNotNull(scrollRect, "the list never produced a ScrollRect");

            var barView = this.BarView();
            var thumb = ThumbRect(barView);
            var drag = barView.GetComponentInChildren<GestureDetectorDragReceiver>(true);
            Assert.IsNotNull(drag, "the thumb never installed a drag receiver");

            // Screen y points up, so a downward drag of the thumb is a negative screen delta.
            var pointer = new PointerEventData(EventSystem.current)
            {
                delta = new Vector2(0f, -DragScreenPixels),
            };

            drag.OnBeginDrag(pointer);

            for (var step = 0; step < DragSteps; step++)
            {
                var metrics = controller.Metrics!.Value;
                var expected = Mathf.Clamp(
                    metrics.PixelOffset
                        + RenderScrollbar.ScrollDeltaForThumbDelta(
                            metrics,
                            Viewport,
                            thumb.rect.height,
                            DragScreenPixels
                        ),
                    0f,
                    metrics.MaxScrollExtent
                );

                drag.OnDrag(pointer);

                Assert.AreEqual(
                    expected,
                    controller.PixelOffset,
                    1f,
                    $"step {step} of the drag moved the list to {controller.PixelOffset}px; "
                        + $"{DragScreenPixels}px of thumb travel over {metrics.ContentExtent}px of "
                        + "content stands for something else"
                );
            }

            drag.OnEndDrag(pointer);

            Assert.Greater(
                controller.PixelOffset,
                Viewport,
                "three drags of a thumb this small have to have covered more than one screenful"
            );

            yield return null;
            yield return null;

            Assert.AreEqual(
                controller.PixelOffset,
                ShownPixelOffset(scrollRect),
                1f,
                $"the ScrollRect shows {ShownPixelOffset(scrollRect)}px while the drag put the "
                    + $"controller at {controller.PixelOffset}px, so the bar and the list disagree "
                    + "about where the list is"
            );
        }

        [UnityTest]
        public IEnumerator TappingTheTrack_PagesTowardTheTap()
        {
            var controller = new ScrollController(this.lifetime.Lifetime);

            this.Host(controller);
            yield return this.Settle();

            var barView = this.BarView();
            var trackRect = (RectTransform)barView.transform;
            var tapReceiver = barView.GetComponent<GestureDetectorTapReceiver>();
            Assert.IsNotNull(tapReceiver, "the track never installed a tap receiver");

            var nearBottom = new Vector3(trackRect.rect.center.x, trackRect.rect.yMin + 20f, 0f);
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(
                    null,
                    trackRect.TransformPoint(nearBottom)
                ),
            };

            tapReceiver.OnPointerClick(pointer);

            Assert.AreEqual(
                Viewport,
                controller.PixelOffset,
                1f,
                $"a tap near the bottom of the track pages one {Viewport}px viewport on, and the list "
                    + $"went to {controller.PixelOffset}px instead"
            );
        }

        [UnityTest]
        public IEnumerator ABarOverContentThatFits_IsNoHitTarget()
        {
            var controller = new ScrollController(this.lifetime.Lifetime);

            this.Host(controller, itemCount: 3);
            yield return this.Settle();

            var barView = this.BarView();

            Assert.IsFalse(
                barView.GetComponent<CanvasGroup>().blocksRaycasts,
                "three rows do not fill a 700px viewport, so the bar draws no thumb; a group that "
                    + "still blocks raycasts eats every pointer event aimed at the list under it"
            );
            Assert.IsFalse(
                barView.GetComponent<InvisibleRaycastTarget>().raycastTarget,
                "and the track itself has nothing to page to, so it is not a hit target either"
            );
        }

        /// <summary>
        ///     Captures the two placements the widget documents, for a reader to look at.
        /// </summary>
        [UnityTest]
        public IEnumerator Snapshots_OfBothPlacements()
        {
            var overlayController = new ScrollController(this.lifetime.Lifetime)
            {
                PixelOffset = 1200f,
            };
            var besideController = new ScrollController(this.lifetime.Lifetime)
            {
                PixelOffset = 1200f,
            };

            using var snapshotter = new WidgetSnapshotter(
                (int)ListWidth,
                (int)Viewport,
                new Color(0.1f, 0.1f, 0.12f)
            );

            var overlay = this.SnapshotPath("scrollbar-overlay.png");
            yield return snapshotter.Capture(
                Overlaid(overlayController, PaintedList(overlayController)),
                overlay
            );

            var beside = this.SnapshotPath("scrollbar-beside.png");
            yield return snapshotter.Capture(
                Beside(besideController, PaintedList(besideController)),
                beside
            );
        }

        // -- Widgets -----------------------------------------------------------------------------

        private static Widget List(ScrollController controller, int itemCount) =>
            new ScrollList
            {
                ScrollController = controller,
                ItemCount = itemCount,
                ItemBuilder = (context, index) => new Container(height: index % 6 == 0 ? 60 : 110),
            };

        /// <summary>The same list, painted, so a capture shows where the list is.</summary>
        private static Widget PaintedList(ScrollController controller) =>
            new ScrollList
            {
                ScrollController = controller,
                ItemCount = 400,
                ItemBuilder = (context, index) =>
                    new Container(height: index % 6 == 0 ? 60 : 110)
                    {
                        BackgroundColor =
                            index % 2 == 0
                                ? new Color(0.22f, 0.24f, 0.30f)
                                : new Color(0.30f, 0.33f, 0.41f),
                    },
            };

        private static Widget Bar(ScrollController controller) =>
            new Scrollbar { Controller = controller, Visibility = ScrollbarVisibility.Always };

        private static Widget Overlaid(ScrollController controller, Widget list) =>
            new Align
            {
                Alignment = Alignment.TopLeft,
                Child = new ZStack
                {
                    Alignment = Alignment.TopLeft,
                    Children =
                    {
                        new SizedBox
                        {
                            Width = ListWidth,
                            Height = Viewport,
                            Child = list,
                        },
                        new Positioned
                        {
                            Top = 0,
                            Bottom = 0,
                            Right = 0,
                            Child = Bar(controller),
                        },
                    },
                },
            };

        private static Widget Beside(ScrollController controller, Widget list) =>
            new Align
            {
                Alignment = Alignment.TopLeft,
                Child = new SizedBox
                {
                    Width = ListWidth,
                    Height = Viewport,
                    // Stretch, so the bar is handed the row's full height: its axis is the one thing
                    // it cannot resolve for itself.
                    Child = new Row
                    {
                        CrossAxisAlignment = CrossAxisAlignment.Stretch,
                        Children =
                        {
                            new Expanded { Child = list },
                            Bar(controller),
                        },
                    },
                },
            };

        // -- Helpers -----------------------------------------------------------------------------

        private static float ShownPixelOffset(ScrollRect scrollRect)
        {
            var range = scrollRect.content.rect.height - scrollRect.viewport.rect.height;
            return (1f - scrollRect.verticalNormalizedPosition) * Mathf.Max(0f, range);
        }

        /// <summary>The distance from the top of the parent's box to the top of this one.</summary>
        private static float TopOf(RectTransform rect) =>
            -rect.anchoredPosition.y - rect.rect.height * (1f - rect.pivot.y);

        private static RectTransform ThumbRect(ScrollbarView barView) =>
            (RectTransform)
                barView.GetComponentInChildren<GestureDetectorDragReceiver>(true).transform;

        private ScrollbarView BarView()
        {
            var barView = this.canvasGo.GetComponentInChildren<ScrollbarView>(true);
            Assert.IsNotNull(barView, "the scrollbar never produced a view");
            return barView;
        }

        private string SnapshotPath(string fileName)
        {
            var path = Path.Combine(Application.temporaryCachePath, fileName);
            TestContext.WriteLine($"snapshot: {path}");
            return path;
        }

        private void Host(ScrollController controller, int itemCount = 400)
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
                _ => Overlaid(controller, List(controller, itemCount))
            );
        }

        private IEnumerator Settle()
        {
            for (var i = 0; i < 6; i++)
            {
                yield return null;
            }
        }
    }
}
