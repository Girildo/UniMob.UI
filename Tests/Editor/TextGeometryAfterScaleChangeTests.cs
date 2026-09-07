using NUnit.Framework;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Why <see cref="UniMobTextMeshProBehaviour" /> discards geometry when it clears its mesh.
    /// </summary>
    /// <remarks>
    ///     TextMeshPro clears an emptied text by detaching its meshes from the canvas renderers, and
    ///     leaves the previous string's glyphs inside them. Its per-frame scale update re-attaches
    ///     those meshes whenever the lossy scale has moved by more than 20%, which a device rotation
    ///     does through the canvas scaler, and its only guard against doing so for an empty text is
    ///     that the parse buffer starts with a terminator. A styled string starts with the style's
    ///     opening tag instead, empty or not, so an emptied label gets its old words back on the first
    ///     rotation. These fixtures pin the fault on TMP's own component and the repair on ours, so a
    ///     TMP that stops re-uploading discarded geometry fails the first and says the override can go.
    /// </remarks>
    public class TextGeometryAfterScaleChangeTests
    {
        /// <summary>Six visible glyphs.</summary>
        private const string Before = "Kolmar";

        /// <summary>Seven visible glyphs -- the space between them draws nothing.</summary>
        private const string After = "Zambia 4";

        private GameObject canvas = null!;
        private TMP_StyleSheet styleSheet = null!;

        [SetUp]
        public void CreateCanvas()
        {
            Assert.IsNotNull(
                TMP_Settings.defaultFontAsset,
                "these fixtures generate real text, so the project needs a default font asset"
            );

            this.canvas = new GameObject("scale-probe-canvas", typeof(Canvas));

            // The sheet is assigned to the component, not only named on it: TMP resolves a text's
            // style by hash through the component's own sheet and then the project default, and a
            // style instance found in neither is silently replaced by Normal, which has no opening
            // tag and so would not exercise the case at all.
            this.styleSheet = ScriptableObject.CreateInstance<TMP_StyleSheet>();
            JsonUtility.FromJsonOverwrite(
                "{\"m_StyleList\":[{\"m_Name\":\"Probe\",\"m_OpeningDefinition\":\"<size=34px>\",\"m_ClosingDefinition\":\"\"}]}",
                this.styleSheet
            );
            this.styleSheet.RefreshStyles();
        }

        [TearDown]
        public void DestroyCanvas()
        {
            Object.DestroyImmediate(this.canvas);
            Object.DestroyImmediate(this.styleSheet);
        }

        /// <summary>
        ///     The fault itself, on TextMeshPro's own component.
        /// </summary>
        [Test]
        public void TextMeshPro_BringsAnEmptiedStyledTextBackOnAScaleChange()
        {
            var text = this.Host<TextMeshProUGUI>();

            Show(text, Before);
            Show(text, string.Empty);
            Assume.That(
                LiveQuads(text),
                Is.EqualTo(0),
                "an emptied text must start out drawing nothing for the scale change to mean anything"
            );

            Rescale(text);

            Assert.AreEqual(
                6,
                LiveQuads(text),
                $"TMP is expected to re-upload the mesh still holding '{Before}' for a text that is "
                    + "empty. Zero quads here means TMP now discards the geometry itself, and the "
                    + "ClearMesh override in UniMobTextMeshProBehaviour is dead weight that should go"
            );
        }

        [Test]
        public void AnEmptiedStyledText_StaysEmptyThroughAScaleChange()
        {
            var text = this.Host<UniMobTextMeshProBehaviour>();

            Show(text, Before);
            Show(text, string.Empty);

            Rescale(text);

            Assert.AreEqual(
                0,
                LiveQuads(text),
                $"'{Before}' is drawn again after the scale change for a text whose value is empty: "
                    + "the geometry survived being cleared"
            );
        }

        [Test]
        public void AnEmptiedText_KeepsNoGeometryInItsMeshes()
        {
            var text = this.Host<UniMobTextMeshProBehaviour>();

            Show(text, Before);
            Show(text, string.Empty);

            Assert.AreEqual(
                0,
                LiveQuads(text.mesh.vertices),
                "the mesh must hold no glyph after the text is emptied, whether or not anything is "
                    + "drawing it: every path that puts a mesh back on screen uploads it as it is"
            );
            Assert.AreEqual(0, text.textInfo.meshInfo[0].vertexCount);
        }

        [Test]
        public void ATextFilledAgainAfterBeingEmptied_IsDrawnAgain()
        {
            var text = this.Host<UniMobTextMeshProBehaviour>();

            Show(text, Before);
            Show(text, string.Empty);
            Show(text, After);

            Assert.AreEqual(
                7,
                LiveQuads(text),
                $"'{After}' has 7 visible glyphs; discarding geometry on an empty value must not get "
                    + "in the way of the next one"
            );
        }

        private T Host<T>()
            where T : TMP_Text
        {
            var host = new GameObject("scale-probe", typeof(RectTransform));
            host.transform.SetParent(this.canvas.transform, false);
            host.GetComponent<RectTransform>().sizeDelta = new Vector2(400, 80);

            var text = host.AddComponent<T>();
            text.styleSheet = this.styleSheet;
            text.textStyle = this.styleSheet.GetStyle("Probe");
            return text;
        }

        /// <summary>Assigns and generates, as the view does at the end of every render.</summary>
        private static void Show(TMP_Text text, string value)
        {
            text.text = value;
            text.ForceMeshUpdate();
        }

        /// <summary>
        ///     Doubles the text's lossy scale and runs the canvas update in which TMP notices it.
        /// </summary>
        private static void Rescale(TMP_Text text)
        {
            text.transform.localScale = new Vector3(2f, 2f, 1f);
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>How many non-degenerate quads the canvas renderer is drawing.</summary>
        private static int LiveQuads(TMP_Text text)
        {
            var mesh = text.canvasRenderer.GetMesh();
            return mesh == null ? 0 : LiveQuads(mesh.vertices);
        }

        private static int LiveQuads(Vector3[] vertices)
        {
            var live = 0;
            for (var i = 0; i + 3 < vertices.Length; i += 4)
            {
                if (vertices[i] != vertices[i + 2])
                {
                    live++;
                }
            }

            return live;
        }
    }
}
