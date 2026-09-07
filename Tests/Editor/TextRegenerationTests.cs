using System;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Why <see cref="UniMob.UI.Internal.Views.LayoutTextView" /> generates its text itself instead
    ///     of leaving it to the canvas update.
    /// </summary>
    /// <remarks>
    ///     TextMeshPro draws only from a rebuild it has been registered for, and it drops that
    ///     registration in silence in two situations while still believing itself dirty -- after which
    ///     nothing ever draws it again. Generating at the end of every render sidesteps the registration
    ///     entirely, and these fixtures pin the two properties of TMP that make that both correct and
    ///     affordable: which flag a resize actually raises, and that an unchanged render raises none.
    /// </remarks>
    public class TextRegenerationTests
    {
        /// <summary>Six visible glyphs.</summary>
        private const string Before = "Kolmar";

        /// <summary>Seven visible glyphs -- the space between them draws nothing.</summary>
        private const string After = "Zambia 4";

        private GameObject canvas = null!;
        private GameObject host = null!;
        private UniMobTextMeshProBehaviour text = null!;

        [SetUp]
        public void CreateText()
        {
            Assert.IsNotNull(
                TMP_Settings.defaultFontAsset,
                "these fixtures generate real text, so the project needs a default font asset"
            );

            this.canvas = new GameObject("regeneration-probe-canvas", typeof(Canvas));
            this.host = new GameObject("regeneration-probe", typeof(RectTransform));
            this.host.transform.SetParent(this.canvas.transform, false);
            this.host.GetComponent<RectTransform>().sizeDelta = new Vector2(400, 80);
            this.text = this.host.AddComponent<UniMobTextMeshProBehaviour>();

            this.text.text = "Randabschluesse versiegeln";
            this.text.ForceMeshUpdate();
        }

        [TearDown]
        public void DestroyText()
        {
            Object.DestroyImmediate(this.canvas);
        }

        [Test]
        public void AResize_RaisesTheLayoutFlagAndNotTheProperties()
        {
            // The whole reason the view asks the composite question. A rotation resizes text, it does
            // not rewrite it: every property assignment the view makes afterwards is a no-op, so a
            // check on havePropertiesChanged alone sees a text with nothing pending and walks past the
            // one that is about to go stale.
            this.host.GetComponent<RectTransform>().sizeDelta = new Vector2(180, 80);

            Assert.IsFalse(
                this.text.havePropertiesChanged,
                "a resize is expected to leave the property flag down -- if TMP starts raising it, the "
                    + "composite question is no longer needed"
            );
            Assert.IsTrue(
                this.text.WantsRegeneration,
                "a resize must still be visible as pending work, or the view will draw the old wrapping"
            );
        }

        [Test]
        public void ARenderThatChangesNothing_LeavesNothingPending()
        {
            // Load-bearing for frame cost. Render re-runs on every layout pass, so if an unchanged pass
            // left work pending, every visible label would regenerate every frame -- the whole cost of
            // a scroll, which is exactly what the textStyle guard in the view exists to avoid.
            this.text.text = "Randabschluesse versiegeln";
            this.text.fontSize = this.text.fontSize;
            this.text.overflowMode = this.text.overflowMode;

            Assert.IsFalse(this.text.WantsRegeneration, "an unchanged render must ask for nothing");
        }

        [Test]
        public void GeneratingAfterAChange_LeavesNothingPendingAndTheNewValueDrawn()
        {
            this.text.text = "Fugen nachziehen";
            Assume.That(this.text.WantsRegeneration, Is.True);

            this.text.ForceMeshUpdate();

            Assert.IsFalse(
                this.text.WantsRegeneration,
                "generating must settle both flags, so that nothing downstream is left waiting on a "
                    + "rebuild registration that may never have been accepted"
            );
            Assert.AreEqual("Fugen nachziehen".Length, this.text.textInfo.characterCount);
        }

        /// <summary>
        ///     The fault itself, reproduced rather than reasoned about.
        /// </summary>
        /// <remarks>
        ///     A write that lands while the canvas is in its graphic-rebuild loop gets its registration
        ///     refused by <c>SetVerticesDirty</c>, silently, after the setter has already raised the
        ///     dirty flag. Nothing rebuilds the component from then on, so the screen keeps the previous
        ///     string for the rest of its life -- and keeps a blank instead, if anything had emptied its
        ///     mesh first. The second half of this test is what the view does at the end of every
        ///     render, and it is the whole reason it does it.
        ///     <para>
        ///         The refusal is asserted, not assumed: should a future uGUI start accepting the
        ///         registration, the first assertion fails and says the generation in
        ///         <c>LayoutTextView.Render</c> can go.
        ///     </para>
        /// </remarks>
        [Test]
        public void AWriteInsideAGraphicRebuild_IsNeverDrawnUntilTheViewGeneratesItItself()
        {
            this.text.text = Before;
            Flush();

            Assert.AreEqual(
                6,
                this.DrawnQuads(),
                $"'{Before}' should be drawn as 6 quads before anything is done to it; the run is not "
                    + "starting from the state it claims to"
            );

            var writer = this.host.AddComponent<WritesDuringTheGraphicRebuild>();
            writer.Write = () => this.text.text = After;
            CanvasUpdateRegistry.RegisterCanvasElementForGraphicRebuild(writer);

            Flush();

            Assert.IsTrue(
                this.text.WantsRegeneration,
                "TMP is expected to raise its dirty flag from the write and then drop the rebuild it "
                    + "asked for. A clean flag here means uGUI now accepts a registration made during "
                    + "the graphic loop, and the generation at the end of the view's render is dead "
                    + "weight that should be deleted"
            );
            Assert.AreEqual(
                6,
                this.DrawnQuads(),
                $"the canvas is still drawing '{Before}' several updates after the view asked for "
                    + $"'{After}', which is the fault: TMP believes it is dirty and nothing will ever "
                    + "ask it to rebuild again"
            );

            // What RegenerateNow does at the end of every LayoutTextView.Render.
            if (this.text.WantsRegeneration)
            {
                this.text.ForceMeshUpdate();
            }

            Assert.AreEqual(
                7,
                this.DrawnQuads(),
                $"'{After}' has 7 visible glyphs, so the view must leave 7 quads drawn no matter what "
                    + "the canvas update did or did not do"
            );
            Assert.IsNotNull(
                this.text.canvasRenderer.GetMesh(),
                "the canvas renderer must be holding geometry, not merely have geometry available to it"
            );
            Assert.IsFalse(this.text.WantsRegeneration, "and nothing must be left pending");
        }

        [Test]
        public void GeneratingAnEmptyValue_LeavesNoCharacters()
        {
            // A rebound view whose new value is empty: generating it hands the canvas renderer no
            // mesh. What the meshes themselves still hold afterwards, and what a later scale change
            // makes of that, is TextGeometryAfterScaleChangeTests' question.
            this.text.text = string.Empty;
            this.text.ForceMeshUpdate();

            Assert.AreEqual(0, this.text.textInfo.characterCount);
            Assert.IsFalse(this.text.WantsRegeneration);
        }

        /// <summary>How many quads the last generation actually produced.</summary>
        private int DrawnQuads()
        {
            var meshInfo = this.text.textInfo.meshInfo;
            return meshInfo == null || meshInfo.Length == 0 ? -1 : meshInfo[0].vertexCount / 4;
        }

        /// <summary>
        ///     Runs the canvas update to completion, twice, so nothing is left queued from set-up and a
        ///     rebuild that was going to happen has had every chance to.
        /// </summary>
        private static void Flush()
        {
            Canvas.ForceUpdateCanvases();
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>
        ///     Writes to the text from inside the canvas's graphic-rebuild loop, which is one of the two
        ///     places TextMeshPro refuses to schedule the rebuild its own setter has just asked for.
        /// </summary>
        private sealed class WritesDuringTheGraphicRebuild : MonoBehaviour, ICanvasElement
        {
            public Action? Write;

            public void Rebuild(CanvasUpdate executing)
            {
                if (executing != CanvasUpdate.PreRender)
                {
                    return;
                }

                var write = this.Write;
                this.Write = null;
                write?.Invoke();
            }

            public void LayoutComplete() { }

            public void GraphicUpdateComplete() { }

            public bool IsDestroyed() => this == null;
        }
    }
}
