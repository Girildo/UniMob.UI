using System;
using System.Collections;
using NUnit.Framework;
using UniMob.Core;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
// UnityEngine.UI is here for CanvasUpdateRegistry and ICanvasElement, and it brings a Text of
// its own; this fixture means the widget.
using Object = UnityEngine.Object;
using Text = UniMob.UI.Widgets.Text;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     A text widget whose value changes while the canvas is mid-rebuild is still drawn.
    /// </summary>
    /// <remarks>
    ///     <c>TextMeshProUGUI.SetVerticesDirty</c> refuses to schedule a rebuild while a graphic rebuild
    ///     is already in flight, and says nothing -- by which point the setter that called it has raised
    ///     the dirty flag, the one state TMP's own per-frame update will not act on. Nothing rebuilds
    ///     the component after that, so the screen keeps the previous string indefinitely, or a blank
    ///     where something had emptied the mesh first.
    ///     <para>
    ///         The sibling EditMode fixture pins that TMP behaves this way and that generating by hand
    ///         recovers it. This one pins the part that cannot be checked without a mounted tree: that
    ///         the view actually does so. Delete the generation from <c>LayoutTextView.Render</c> and
    ///         this is the test that fails.
    ///     </para>
    /// </remarks>
    public class TextRegenerationViewTests
    {
        /// <summary>Six visible glyphs.</summary>
        private const string Before = "Kolmar";

        /// <summary>Seven visible glyphs -- the space between them draws nothing.</summary>
        private const string After = "Zambia 4";

        private LifetimeController lifetime = null!;
        private GameObject canvasGo = null!;
        private MutableAtom<string> value = null!;

        [SetUp]
        public void SetUp()
        {
            this.lifetime = new LifetimeController();
            this.value = Atom.Value(Before);

            this.canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            this.canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        }

        [TearDown]
        public void TearDown()
        {
            this.lifetime.Dispose();
            Object.DestroyImmediate(this.canvasGo);
        }

        [UnityTest]
        public IEnumerator AValueChangedInsideAGraphicRebuild_IsStillDrawn()
        {
            this.Host();
            yield return Settle();

            var text = this.canvasGo.GetComponentInChildren<UniMobTextMeshProBehaviour>();
            Assert.IsNotNull(text, "the tree never produced a text component to look at");
            Assert.AreEqual(
                6,
                DrawnQuads(text),
                $"'{Before}' should be drawn as 6 quads before anything is done to it; this run is not "
                    + "starting from the state it claims to"
            );

            // Drives the whole UniMob render from inside the canvas's graphic-rebuild loop, so the
            // view's assignment asks for a rebuild at the one moment uGUI will not grant one.
            var drawnInsideTheRebuild = -1;
            var valueInsideTheRebuild = string.Empty;

            var writer = this.canvasGo.AddComponent<RendersDuringTheGraphicRebuild>();
            writer.Work = () =>
            {
                this.value.Value = After;

                // Twice, and not once: the write invalidates the root reaction, and the reaction is
                // what invalidates the view's own render atom -- which the scheduler then queues for
                // the pass after this one. One Sync rebuilds the tree and leaves the label untouched.
                AtomScheduler.Sync();
                AtomScheduler.Sync();

                // Sampled here and not after the frame, which is the whole difficulty of catching this:
                // a live tree dirties its labels again for all sorts of reasons, and any of them lands
                // outside the rebuild loop, is granted, and repairs the fault before a later assertion
                // could see it. Inside the loop is the only place the view is alone with the problem.
                valueInsideTheRebuild = text.text;
                drawnInsideTheRebuild = DrawnQuads(text);
            };
            CanvasUpdateRegistry.RegisterCanvasElementForGraphicRebuild(writer);

            yield return Settle();

            Assert.IsTrue(
                writer.Ran,
                "the writer never fired, so nothing in this run happened inside a graphic rebuild and "
                    + "the case under test was never exercised"
            );
            Assert.AreEqual(
                After,
                valueInsideTheRebuild,
                "the view never rendered inside the rebuild loop, so this run proves nothing about what "
                    + "happens when it does"
            );
            Assert.AreEqual(
                7,
                drawnInsideTheRebuild,
                $"the view rendered '{After}' while the canvas was mid-rebuild and left only "
                    + $"{drawnInsideTheRebuild} quads drawn -- still '{Before}'. TMP dropped the rebuild "
                    + "the assignment asked for and will never ask again, so the view has to generate "
                    + "the text itself rather than wait to be asked"
            );
        }

        /// <summary>How many quads the last generation actually produced.</summary>
        private static int DrawnQuads(UniMobTextMeshProBehaviour text)
        {
            var meshInfo = text.textInfo.meshInfo;
            return meshInfo == null || meshInfo.Length == 0 ? -1 : meshInfo[0].vertexCount / 4;
        }

        private void Host()
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
                    // Fixed, so the new value cannot resize the label. A resize would dirty it again on
                    // the next layout pass -- outside the rebuild loop, where the registration is
                    // accepted -- and heal the very fault this test exists to catch.
                    Child = new SizedBox
                    {
                        Width = 400,
                        Height = 60,
                        Child = new Text { Value = this.value.Value, FontSize = 24 },
                    },
                }
            );
        }

        private static IEnumerator Settle()
        {
            for (var i = 0; i < 4; i++)
            {
                yield return null;
            }
        }

        /// <summary>
        ///     Runs one callback from inside the canvas's graphic-rebuild loop, once.
        /// </summary>
        private sealed class RendersDuringTheGraphicRebuild : MonoBehaviour, ICanvasElement
        {
            public Action? Work;

            public bool Ran { get; private set; }

            public void Rebuild(CanvasUpdate executing)
            {
                if (executing != CanvasUpdate.PreRender || this.Work == null)
                {
                    return;
                }

                var work = this.Work;
                this.Work = null;
                this.Ran = true;
                work.Invoke();
            }

            public void LayoutComplete() { }

            public void GraphicUpdateComplete() { }

            public bool IsDestroyed() => this == null;
        }
    }
}
