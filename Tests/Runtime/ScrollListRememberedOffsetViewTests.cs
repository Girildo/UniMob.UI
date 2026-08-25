using System.Collections;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     A list mounted against a <see cref="ScrollController"/> that already remembers an offset
    ///     must show that offset once it has rendered.
    /// </summary>
    /// <remarks>
    ///     The view applies the controller's offset when it is activated, which is before its content
    ///     has been sized for this list, so that first write lands on a scroll range of zero. The
    ///     render object still builds its window for the remembered offset, and a ScrollRect left at
    ///     the top then shows a region nothing was built for. Room tabs hit this on every return to a
    ///     scrolled room: the store keeps the controller, the list is inflated afresh.
    /// </remarks>
    public class ScrollListRememberedOffsetViewTests
    {
        private const float Viewport = 700f;
        private const float RememberedOffset = 5000f;

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
        public IEnumerator AListMountedAgainstARememberedOffset_ShowsThatOffset()
        {
            var controller = new ScrollController(this.lifetime.Lifetime)
            {
                PixelOffset = RememberedOffset,
            };

            this.Host(controller);

            yield return this.Settle();

            var scrollRect = this.canvasGo.GetComponentInChildren<ScrollRect>();
            Assert.IsNotNull(scrollRect, "the list never produced a ScrollRect");

            var shown = ShownPixelOffset(scrollRect);

            Assert.AreEqual(
                RememberedOffset,
                controller.PixelOffset,
                1f,
                "the controller's remembered offset must survive the mount; a ScrollRect settling at "
                    + "the top must not write its own position back over it"
            );
            Assert.AreEqual(
                RememberedOffset,
                shown,
                1f,
                $"the ScrollRect shows {shown}px while the window was built for {RememberedOffset}px, "
                    + "so the viewport holds a region nothing was built for"
            );
        }

        private static float ShownPixelOffset(ScrollRect scrollRect)
        {
            var range = scrollRect.content.rect.height - scrollRect.viewport.rect.height;
            return (1f - scrollRect.verticalNormalizedPosition) * Mathf.Max(0f, range);
        }

        private void Host(ScrollController controller)
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
                            ItemCount = 400,
                            ItemBuilder = (context, index) =>
                                new Container(height: index % 6 == 0 ? 60 : 110),
                        },
                    },
                }
            );
        }

        private IEnumerator Settle()
        {
            for (var i = 0; i < 4; i++)
            {
                yield return null;
            }
        }
    }
}
