using System.Collections;
using NUnit.Framework;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     <see cref="ScrollController.ScrollTo(Key, float, ScrollToPosition?, Easing)"/> with
    ///     <see cref="ScrollToPosition.Nearest"/> against a mounted list: it moves the least distance that
    ///     shows the item, including an item the source has gained in the same frame, before the list has
    ///     been rebuilt for it.
    /// </summary>
    public class ScrollListScrollToNearestViewTests
    {
        private const float Viewport = 700f;
        private const float ItemHeight = 100f;
        private const int InitialCount = 20;
        private const float AnimationSeconds = 0.3f;

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
        public IEnumerator AnItemTheSourceGainedThisFrame_IsShown_WithoutAnimation()
        {
            yield return this.AppendAndScrollToIt(duration: 0f);
        }

        [UnityTest]
        public IEnumerator AnItemTheSourceGainedThisFrame_IsShown_WhenAnimated()
        {
            yield return this.AppendAndScrollToIt(duration: AnimationSeconds);
        }

        [UnityTest]
        public IEnumerator AnItemAlreadyFullyInView_LeavesTheListWhereItIs()
        {
            var controller = new ScrollController(this.lifetime.Lifetime);
            this.Host(controller, Atom.Value(InitialCount));
            yield return Settle();

            controller.ScrollTo(Key.Of(3), 0f, ScrollToPosition.Nearest);
            yield return Settle();

            Assert.AreEqual(
                0f,
                controller.PixelOffset,
                1f,
                "item 3 spans [300, 400) inside the viewport [0, 700), so nothing should move"
            );
        }

        [UnityTest]
        public IEnumerator AnItemAboveTheViewport_StartsAtTheTopOfIt()
        {
            var controller = new ScrollController(this.lifetime.Lifetime) { PixelOffset = 1000f };
            this.Host(controller, Atom.Value(InitialCount));
            yield return Settle();
            Assume.That(controller.PixelOffset, Is.EqualTo(1000f).Within(1f));

            controller.ScrollTo(Key.Of(2), 0f, ScrollToPosition.Nearest);
            yield return Settle();

            Assert.AreEqual(
                2 * ItemHeight,
                controller.PixelOffset,
                1f,
                "item 2 spans [200, 300), above the viewport [1000, 1700), so it should start at the top"
            );
        }

        private IEnumerator AppendAndScrollToIt(float duration)
        {
            var controller = new ScrollController(this.lifetime.Lifetime);
            var count = Atom.Value(InitialCount);
            this.Host(controller, count);
            yield return Settle();
            Assume.That(
                controller.PixelOffset,
                Is.EqualTo(0f).Within(1f),
                "the list starts at the top"
            );

            // The source gains the item and the scroll is asked for in the same frame, before the list
            // has been rebuilt for it.
            count.Value = InitialCount + 1;
            Assert.IsTrue(
                controller.ScrollTo(Key.Of(InitialCount), duration, ScrollToPosition.Nearest),
                "a mounted list whose resolver knows the key should accept the request"
            );

            yield return new WaitForSecondsRealtime(duration);
            yield return Settle();

            var bottom = (InitialCount + 1) * ItemHeight - Viewport;
            Assert.AreEqual(
                bottom,
                controller.PixelOffset,
                1f,
                "the new last item should end at the bottom of the viewport"
            );
            Assert.AreEqual(
                bottom,
                ShownPixelOffset(this.canvasGo),
                1f,
                "the ScrollRect should show the offset the controller holds"
            );
        }

        private static float ShownPixelOffset(GameObject root)
        {
            var scrollRect = root.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
            var range = scrollRect.content.rect.height - scrollRect.viewport.rect.height;
            return (1f - scrollRect.verticalNormalizedPosition) * Mathf.Max(0f, range);
        }

        private void Host(ScrollController controller, MutableAtom<int> count)
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
                        Child = new ScrollList
                        {
                            ScrollController = controller,
                            MovementType = MovementType.Clamped,
                            ItemCount = count.Value,
                            KeyToIndexResolver = key => IndexOf(key, count.Value),
                            ItemBuilder = (context, index) =>
                                new Container(height: ItemHeight) { Key = Key.Of(index) },
                        },
                    },
                }
            );
        }

        private static int? IndexOf(Key key, int count)
        {
            for (var index = 0; index < count; index++)
            {
                if (Key.Of(index).Equals(key))
                    return index;
            }

            return null;
        }

        private static IEnumerator Settle()
        {
            for (var i = 0; i < 6; i++)
            {
                yield return null;
            }
        }
    }
}
