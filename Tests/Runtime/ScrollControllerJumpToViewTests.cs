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
    ///     <see cref="ScrollController.JumpTo"/> puts the list at an offset and leaves it there, against
    ///     both of the things that move a list on their own: the ScrollRect's inertia and a running
    ///     <see cref="ScrollController.ScrollTo(int, float, ScrollToPosition?, Easing)"/> animation.
    /// </summary>
    /// <remarks>
    ///     Needs the real player loop: inertia is Unity's own LateUpdate integration of
    ///     <c>ScrollRect.velocity</c>, and the scroll animation is a coroutine. Neither exists in a
    ///     headless mount, which is why the controller-side arithmetic is pinned in EditMode and only
    ///     the "it actually stops" half lives here.
    /// </remarks>
    public class ScrollControllerJumpToViewTests
    {
        private const float Viewport = 700f;
        private const float StartOffset = 1000f;
        private const float JumpTarget = 3000f;

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
        public IEnumerator AJump_KillsInertia_AndStays()
        {
            var controller = new ScrollController(this.lifetime.Lifetime)
            {
                PixelOffset = StartOffset,
            };

            this.Host(controller);

            yield return this.Settle();

            var scrollRect = this.canvasGo.GetComponentInChildren<ScrollRect>();
            Assert.IsNotNull(scrollRect, "the list never produced a ScrollRect");

            var beforeFlick = ShownPixelOffset(scrollRect);

            // A flick the user could give it: Unity integrates this in LateUpdate, so the list keeps
            // moving on its own for as long as the velocity takes to decay.
            scrollRect.velocity = new Vector2(0f, 2000f);

            yield return null;
            yield return null;

            var flicked = ShownPixelOffset(scrollRect);

            Assert.Greater(
                Mathf.Abs(flicked - beforeFlick),
                1f,
                $"the list did not move under inertia (still at {flicked}px), so the rest of this test "
                    + "would prove nothing about a jump stopping it"
            );

            Assert.IsTrue(
                controller.JumpTo(JumpTarget),
                "an attached, laid out list must accept a jump"
            );

            yield return null;
            yield return null;
            yield return null;

            Assert.AreEqual(
                JumpTarget,
                controller.PixelOffset,
                1f,
                "the controller is the one place the jump offset is written, and inertia must not write "
                    + "back over it"
            );
            Assert.AreEqual(
                JumpTarget,
                ShownPixelOffset(scrollRect),
                1f,
                $"the ScrollRect shows {ShownPixelOffset(scrollRect)}px while the window was built for "
                    + $"{JumpTarget}px, so the viewport holds a region nothing was built for"
            );

            for (var frame = 0; frame < 5; frame++)
            {
                yield return null;

                Assert.AreEqual(
                    JumpTarget,
                    ShownPixelOffset(scrollRect),
                    1f,
                    $"the list drifted {frame + 1} frames after the jump: the flick's velocity survived "
                        + "it and is still being integrated"
                );
            }
        }

        [UnityTest]
        public IEnumerator AJump_CutsShortARunningScrollTo()
        {
            var controller = new ScrollController(this.lifetime.Lifetime);

            this.Host(controller);

            yield return this.Settle();

            var scrollRect = this.canvasGo.GetComponentInChildren<ScrollRect>();
            Assert.IsNotNull(scrollRect, "the list never produced a ScrollRect");

            // Linear, so the animation has covered a measurable distance after two frames rather than
            // the few pixels an ease-in curve spends its first frames on.
            Assert.IsTrue(
                controller.ScrollTo(300, duration: 2f, easing: Ease.Linear),
                "an attached list must accept a scroll-to"
            );

            yield return null;
            yield return null;

            var animating = controller.PixelOffset;

            Assert.Greater(
                animating,
                1f,
                $"the animation had not started moving (still at {animating}px), so a jump interrupting "
                    + "it would prove nothing"
            );

            controller.JumpTo(JumpTarget);

            yield return null;
            yield return null;
            yield return null;

            Assert.AreEqual(
                JumpTarget,
                controller.PixelOffset,
                1f,
                "the jump must win over the animation that was running when it was issued"
            );

            for (var frame = 0; frame < 5; frame++)
            {
                yield return null;

                Assert.AreEqual(
                    JumpTarget,
                    controller.PixelOffset,
                    1f,
                    $"the list moved again {frame + 1} frames after the jump, well inside the two seconds "
                        + "the animation was asked for: the animation was never stopped"
                );
            }
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
            for (var i = 0; i < 6; i++)
            {
                yield return null;
            }
        }
    }
}
