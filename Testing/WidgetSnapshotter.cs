using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UniMob.Core;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Renders a widget tree to a PNG: mounts it with <see cref="UniMobUI.RunApp"/> into a
    ///     <see cref="ViewPanel"/> under a Canvas, waits until it has nothing left to do, and reads the
    ///     render back from an offscreen <see cref="RenderTexture"/> at a fixed logical size.
    /// </summary>
    /// <remarks>
    ///     Mounting through the ordinary hosting path rather than a shortcut is the point: what the
    ///     picture shows was laid out and rendered the way a mounted tree always is, so a layout that is
    ///     wrong in the picture is wrong on screen. One instance owns one rig -- render texture, camera,
    ///     canvas and panel -- for as many captures as it is asked for, and tears it down on
    ///     <see cref="Dispose"/>. A capture advances real frames, so it belongs to a PlayMode fixture.
    /// </remarks>
    public sealed class WidgetSnapshotter : IDisposable
    {
        /// <summary>Frames a capture may spend waiting for the reactive graph to go quiet.</summary>
        private const int SettleFrameLimit = 300;

        /// <summary>How far the camera sits from the canvas plane, in canvas units.</summary>
        private const float CanvasDistance = 100f;

        /// <summary>Characters of a text quoted back in a failure, before it is cut short.</summary>
        private const int ExcerptLength = 48;

        // Rich text tags are markup, not glyphs: asking an atlas for '<', 'b' and '>' invites a failure
        // about characters no font is obliged to have, on text that renders perfectly.
        private static readonly Regex RichTextTag = new Regex("<[^<>]*>", RegexOptions.Compiled);

        private readonly RenderTexture _target;
        private readonly GameObject _cameraObject;
        private readonly Camera _camera;
        private readonly GameObject _canvasObject;
        private readonly ViewPanel _panel;
        private readonly int _width;
        private readonly int _height;

        private bool _disposed;

        /// <summary>
        ///     Builds the rig. <paramref name="background"/> is what shows through wherever the widget
        ///     tree paints nothing, so a colour no widget uses makes the tree's own bounds readable.
        /// </summary>
        public WidgetSnapshotter(int width, int height, Color background)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width, "Must be positive.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), height, "Must be positive.");
            }

            _width = width;
            _height = height;

            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "WidgetSnapshotTarget",
            };
            _target.Create();

            _cameraObject = new GameObject("WidgetSnapshotCamera");
            _cameraObject.transform.position = new Vector3(0f, 0f, -CanvasDistance);
            _camera = _cameraObject.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = height / 2f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 1000f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = background;
            _camera.targetTexture = _target;

            _canvasObject = new GameObject(
                "WidgetSnapshotCanvas",
                typeof(RectTransform),
                typeof(Canvas)
            );
            var canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _camera;
            canvas.planeDistance = CanvasDistance;

            var panelObject = new GameObject("ViewPanel", typeof(RectTransform));
            panelObject.transform.SetParent(_canvasObject.transform, false);

            // Sized before the panel component exists, so the first render already has constraints to
            // lay out against rather than a zero-sized rect it has to be invalidated out of.
            var panelRect = (RectTransform)panelObject.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(width, height);
            panelRect.anchoredPosition = Vector2.zero;
            _panel = panelObject.AddComponent<ViewPanel>();
        }

        /// <summary>
        ///     Mounts <paramref name="root"/>, waits until it has nothing left to do, and writes the
        ///     render to <paramref name="outputPath"/> as a PNG, creating the directory if it is
        ///     missing. The tree is unmounted again before the capture returns, so the rig is free for
        ///     the next one.
        /// </summary>
        /// <exception cref="TimeoutException">
        ///     The tree never went quiet. A finding, not a slow capture: something schedules reactive
        ///     work on every frame for as long as it is mounted.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///     A font cannot draw a character the tree asks for (see <see cref="VerifyGlyphs"/>), or the
        ///     tree left work running after it was unmounted. The second is checked here rather than
        ///     left to the next capture, which would otherwise be the one that fails.
        /// </exception>
        public IEnumerator Capture(Widget root, string outputPath)
        {
            Prepare(root, outputPath);

            var mount = new LifetimeController();
            try
            {
                Mount(mount, root);

                var settled = false;
                for (var frame = 0; frame < SettleFrameLimit && !settled; frame++)
                {
                    yield return null;
                    settled = Zone.IsQuiescent;
                }

                if (!settled)
                {
                    throw new TimeoutException(
                        $"'{outputPath}' did not settle within {SettleFrameLimit} frames: "
                            + Outstanding()
                            + " on every one of them."
                    );
                }

                Shoot(outputPath);

                mount.Dispose();
                yield return null;

                if (!Zone.IsQuiescent)
                {
                    throw new InvalidOperationException(
                        $"'{outputPath}' left work running after it was unmounted: "
                            + Outstanding()
                            + ". Something in the tree outlives the tree, which will keep every "
                            + "later capture from settling. A pooled view still driving an "
                            + "animation is the usual cause."
                    );
                }
            }
            finally
            {
                mount.Dispose();
            }
        }

        private static string Outstanding() =>
            AtomScheduler.HasPendingWork
                ? "the reactive graph had queued work"
                : "callbacks were queued for the following frame";

        private void Prepare(Widget root, string outputPath)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                throw new ArgumentException("An output path is required.", nameof(outputPath));
            }

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WidgetSnapshotter));
            }
        }

        private void Mount(LifetimeController mount, Widget root) =>
            UniMobUI.RunApp(mount.Lifetime, _panel, _ => root);

        /// <summary>Lays the tree out, fills the atlases, checks them, and writes the picture.</summary>
        /// <remarks>
        ///     Neither of the two waits here has anything to await: a Canvas rebuild and a TMP atlas
        ///     rebuild both run off <c>Canvas.willRenderCanvases</c>, so they are driven rather than
        ///     waited for.
        /// </remarks>
        private void Shoot(string outputPath)
        {
            Canvas.ForceUpdateCanvases();
            ForceGlyphs();
            Canvas.ForceUpdateCanvases();
            VerifyGlyphs();

            _camera.Render();
            WritePng(outputPath);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _camera.targetTexture = null;
            UnityEngine.Object.Destroy(_canvasObject);
            UnityEngine.Object.Destroy(_cameraObject);
            _target.Release();
            UnityEngine.Object.Destroy(_target);
        }

        /// <summary>
        ///     Adds every character the mounted text asks for to the dynamic atlases that own them, and
        ///     regenerates the meshes that were laid out without them.
        /// </summary>
        /// <remarks>
        ///     Whether a font managed to add them all is not asked here: a character this font lacks may
        ///     still come from a fallback, which is what <see cref="VerifyGlyphs"/> accounts for.
        /// </remarks>
        private void ForceGlyphs()
        {
            var texts = _panel.GetComponentsInChildren<TMP_Text>();

            foreach (var text in texts)
            {
                var font = text.font;

                // A static atlas cannot be added to, and asking logs a warning saying so. Whether it
                // already holds what it needs is the verification pass's question, not this one's.
                if (font == null || font.atlasPopulationMode == AtlasPopulationMode.Static)
                {
                    continue;
                }

                var needed = GlyphsNeededBy(text);
                if (needed.Length > 0)
                {
                    font.TryAddCharacters(needed, out _);
                }
            }

            foreach (var text in texts)
            {
                text.ForceMeshUpdate();
            }
        }

        /// <summary>
        ///     Fails the capture if any mounted text asks for a character its font and that font's
        ///     fallbacks cannot draw, naming the characters.
        /// </summary>
        /// <remarks>
        ///     A missing glyph does not leave a hole in the picture: TMP substitutes a box, so the
        ///     capture comes out plausible and wrong, and a reader comparing it against a design
        ///     reference reads the box as a font choice.
        /// </remarks>
        private void VerifyGlyphs()
        {
            var problems = new List<string>();

            foreach (var text in _panel.GetComponentsInChildren<TMP_Text>())
            {
                var font = text.font;
                if (font == null)
                {
                    continue;
                }

                var needed = GlyphsNeededBy(text);
                if (needed.Length == 0 || font.HasCharacters(needed, out uint[] missing, true))
                {
                    continue;
                }

                problems.Add(
                    $"'{font.name}' cannot draw {Describe(missing)}, asked for by "
                        + $"{text.name} in \"{Excerpt(needed)}\""
                );
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "A font is missing characters this capture needs, and TMP draws a character it "
                        + "cannot find as a box rather than as nothing:\n  "
                        + string.Join("\n  ", problems)
                );
            }
        }

        /// <summary>The characters <paramref name="text"/> asks its font to draw.</summary>
        /// <remarks>
        ///     Taken from what the caller set rather than from <c>TMP_Text.GetParsedText</c>, which
        ///     reports the text as generated: a character with no glyph has already been replaced by a
        ///     box there, so checking that would pass on exactly the render this exists to catch.
        /// </remarks>
        private static string GlyphsNeededBy(TMP_Text text)
        {
            var source = text.text;
            if (string.IsNullOrEmpty(source))
            {
                return string.Empty;
            }

            if (text.richText)
            {
                source = RichTextTag.Replace(source, string.Empty);
            }

            var glyphs = new StringBuilder(source.Length);
            foreach (var character in source)
            {
                // Line breaks and tabs are layout instructions, which TMP acts on without ever looking
                // them up in an atlas. A font with no glyph for one of them is not a fault.
                if (!char.IsControl(character))
                {
                    glyphs.Append(character);
                }
            }

            return glyphs.ToString();
        }

        private static string Describe(IReadOnlyList<uint> codePoints)
        {
            var described = new List<string>(codePoints.Count);

            foreach (var codePoint in codePoints)
            {
                var isQuotable =
                    codePoint >= 0x20
                    && codePoint <= 0xFFFF
                    && (codePoint < 0xD800 || codePoint > 0xDFFF);

                described.Add(
                    isQuotable ? $"U+{codePoint:X4} '{(char)codePoint}'" : $"U+{codePoint:X4}"
                );
            }

            return string.Join(", ", described);
        }

        private static string Excerpt(string value) =>
            value.Length <= ExcerptLength ? value : value.Substring(0, ExcerptLength) + "...";

        private void WritePng(string outputPath)
        {
            var previousActive = RenderTexture.active;
            var texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false);

            try
            {
                RenderTexture.active = _target;
                texture.ReadPixels(new Rect(0f, 0f, _width, _height), 0, 0);
                texture.Apply();
            }
            finally
            {
                RenderTexture.active = previousActive;
            }

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var png = texture.EncodeToPNG();
            UnityEngine.Object.Destroy(texture);

            File.WriteAllBytes(outputPath, png);
            Debug.Log(
                $"[WidgetSnapshotter] Wrote {outputPath} ({_width}x{_height}, {png.Length} bytes)"
            );
        }
    }
}
