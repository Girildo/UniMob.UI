#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using TMPro;
using UniMob.UI.Widgets;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Text = UniMob.UI.Widgets.Text;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Every text drawn after a device rotation is the text its widget asked for, in the box
    ///     layout gave it.
    /// </summary>
    /// <remarks>
    ///     A rotation is the one resize a phone does in a single step: both axes change, the canvas
    ///     scale factor moves by more than the 20% at which TextMeshPro rewrites SDF scale in place,
    ///     and a responsive tree rebuilds itself while pooled views are handed from one string to
    ///     another. This fixture rotates the Game view for real, through the play mode window, over a
    ///     tree with the shapes the app uses -- clamped titles, wrapped paragraphs, labels that are
    ///     empty in one orientation and not in the other, a lazy list whose window changes -- and
    ///     then audits every text component against a fresh generation of its own string.
    /// </remarks>
    public class TextRotationViewTests
    {
        private const string ProbeResolutionName = "UniMob rotation probe";

        private LifetimeController lifetime = null!;
        private GameObject canvasGo = null!;
        private string? styleName;
        private ScrollController scrollController = null!;

        private PlayModeWindow.PlayModeViewTypes originalViewType;
        private uint originalWidth;
        private uint originalHeight;
        private int originalRenderFrameInterval;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(
                TMP_Settings.defaultFontAsset,
                "this fixture generates real text, so the project needs a default font asset"
            );

            // The Game view, not the device simulator: only the former takes a resolution from here.
            this.originalViewType = PlayModeWindow.GetViewType();
            PlayModeWindow.SetViewType(PlayModeWindow.PlayModeViewTypes.GameView);
            PlayModeWindow.GetRenderingResolution(out this.originalWidth, out this.originalHeight);
            this.originalRenderFrameInterval = OnDemandRendering.renderFrameInterval;

            this.lifetime = new LifetimeController();
            this.scrollController = new ScrollController(this.lifetime.Lifetime);

            // A style with an opening definition, as every app text carries one: a styled string
            // starts with that tag rather than with a terminator, which is a different path through
            // TMP from the Normal style. Taken from the project's default sheet, because that is the
            // only sheet the component resolves a style through, so a sheet made here would be
            // silently replaced by Normal.
            this.styleName = FindStyleWithAnOpeningDefinition(TMP_Settings.defaultStyleSheet);
            Assume.That(
                this.styleName,
                Is.Not.Null,
                "this fixture needs the project's default TMP style sheet to define at least one "
                    + "style with an opening definition"
            );

            this.canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            var canvas = this.canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = this.canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0f;

            this.canvasGo.AddComponent<MatchesOrientation>();
        }

        [TearDown]
        public void TearDown()
        {
            OnDemandRendering.renderFrameInterval = this.originalRenderFrameInterval;
            this.lifetime.Dispose();
            Object.DestroyImmediate(this.canvasGo);

            PlayModeWindow.SetCustomRenderingResolution(
                this.originalWidth,
                this.originalHeight,
                ProbeResolutionName
            );
            PlayModeWindow.SetViewType(this.originalViewType);
        }

        [UnityTest]
        public IEnumerator EveryTextSurvivesRotation_AtFullFrameRate()
        {
            yield return this.RotateAndAudit(renderFrameInterval: 1);
        }

        [UnityTest]
        public IEnumerator EveryTextSurvivesRotation_WhileFramesAreSkipped()
        {
            yield return this.RotateAndAudit(renderFrameInterval: 3);
        }

        private IEnumerator RotateAndAudit(int renderFrameInterval)
        {
            yield return SetResolution(1080, 1920);

            this.Host();
            yield return Settle(6);

            OnDemandRendering.renderFrameInterval = renderFrameInterval;

            var findings = new List<string>();

            // (width, height, frames to let the list settle before rotating): a rotation after the
            // page has come to rest, one landing while the list is still moving, and two in a row.
            var rotations = new[]
            {
                (1920, 1080, 2),
                (1080, 1920, 0),
                (1920, 1080, 2),
                (1080, 1920, 2),
                (1920, 1080, 0),
                (1080, 1920, 0),
            };

            for (var i = 0; i < rotations.Length; i++)
            {
                var (width, height, settleFrames) = rotations[i];

                // Move the list between rotations so the lazy window changes and pooled views are
                // handed from one item to another, as a scrolled page is.
                this.scrollController.ScrollTo(i % 2 == 0 ? 12 : 0, duration: 0.2f);
                yield return Settle(settleFrames);

                yield return SetResolution(width, height);
                yield return Settle(6);

                this.Audit($"rotation {i + 1} to {width}x{height}", findings);
            }

            OnDemandRendering.renderFrameInterval = 1;

            Assert.IsEmpty(
                findings,
                "texts drawn wrongly after rotating:\n" + string.Join("\n", findings)
            );
        }

        /// <summary>
        ///     Compares every text component on screen against its state and against a fresh
        ///     generation of its own string, and describes each disagreement.
        /// </summary>
        private void Audit(string moment, List<string> findings)
        {
            var texts = this.canvasGo.GetComponentsInChildren<UniMobTextMeshProBehaviour>(false);
            Assert.IsNotEmpty(texts, "the tree produced no text component to look at");

            var empties = 0;
            var findingsBefore = findings.Count;

            foreach (var text in texts)
            {
                var view = text.GetComponent<IView>();
                var state = view?.Source as ITextState;
                if (state == null)
                {
                    continue;
                }

                var expected = state.Value;
                var where = Describe(text, expected);
                if (expected.Length == 0)
                {
                    empties++;
                }

                if (text.text != expected)
                {
                    findings.Add(
                        $"{moment}: {where} -- the component holds '{text.text}' while the state says "
                            + $"'{expected}': the view never rendered this state"
                    );
                }

                var drawn = DrawnCharacters(text);
                if (!IsHowTextIsDrawn(expected, drawn))
                {
                    findings.Add($"{moment}: {where} -- the generated characters read '{drawn}'");
                }

                var rendererMesh = text.canvasRenderer.GetMesh();
                var liveQuads = rendererMesh == null ? 0 : LiveQuads(rendererMesh.vertices);
                var expectsGlyphs = expected.Trim().Length > 0;

                if (expectsGlyphs && rendererMesh == null)
                {
                    findings.Add($"{moment}: {where} -- the canvas renderer holds no mesh: blank");
                }
                else if (!expectsGlyphs && liveQuads > 0)
                {
                    findings.Add(
                        $"{moment}: {where} -- the canvas renderer draws {liveQuads} quads for an "
                            + "empty string: phantom glyphs"
                    );
                }

                if (rendererMesh == null)
                {
                    continue;
                }

                // Whatever is drawn must be what generating the same string in the same box draws
                // now. Regenerating heals the component, so this comparison is the last thing done
                // to it.
                var before = rendererMesh.vertices;
                var bounds = text.rectTransform.rect;
                var flags = text.WantsRegeneration;
                text.ForceMeshUpdate(true, true);
                var after = text.mesh.vertices;

                var stale = DifferingQuads(before, after);
                if (stale > 0)
                {
                    findings.Add(
                        $"{moment}: {where} -- {stale} quads differ from a fresh generation "
                            + $"(box {bounds.size}, pending regeneration: {flags}): stale geometry"
                    );
                }
            }

            var canvas = this.canvasGo.GetComponent<Canvas>();
            Debug.Log(
                $"[TextRotationViewTests] {moment}: screen {Screen.width}x{Screen.height}, canvas "
                    + $"scale {canvas.scaleFactor:0.000}, render interval "
                    + $"{OnDemandRendering.renderFrameInterval}, audited {texts.Length} texts "
                    + $"({empties} empty), {findings.Count - findingsBefore} findings"
            );
        }

        private static string Describe(UniMobTextMeshProBehaviour text, string expected)
        {
            var rect = text.rectTransform.rect;
            return $"'{Truncate(expected)}' in {rect.width:0}x{rect.height:0} "
                + $"(scale {text.transform.lossyScale.y:0.000}, active {text.isActiveAndEnabled}, "
                + $"culled {text.canvasRenderer.cull})";
        }

        private static string Truncate(string value) =>
            value.Length <= 24 ? value : value.Substring(0, 21) + "...";

        /// <summary>The characters the last generation laid out, ellipsis included.</summary>
        private static string DrawnCharacters(TMP_Text text)
        {
            var info = text.textInfo;
            var sb = new StringBuilder(info.characterCount);
            for (var i = 0; i < info.characterCount; i++)
            {
                sb.Append(info.characterInfo[i].character);
            }

            return sb.ToString();
        }

        /// <summary>
        ///     Whether <paramref name="drawn"/> is how TMP lays out <paramref name="expected"/>: the
        ///     string itself, or a prefix of it closed by an ellipsis where it was clamped.
        /// </summary>
        private static bool IsHowTextIsDrawn(string expected, string drawn)
        {
            if (drawn == expected)
            {
                return true;
            }

            if (drawn.Length > 0 && drawn[drawn.Length - 1] == '…')
            {
                var kept = drawn.Substring(0, drawn.Length - 1).TrimEnd();
                return expected.StartsWith(kept, StringComparison.Ordinal);
            }

            // TMP drops trailing whitespace and control characters from what it lays out.
            return expected.TrimEnd() == drawn.TrimEnd();
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

        private static int DifferingQuads(Vector3[] before, Vector3[] after)
        {
            var quads = Math.Max(before.Length, after.Length) / 4;
            var differing = 0;
            for (var q = 0; q < quads; q++)
            {
                for (var v = 0; v < 4; v++)
                {
                    var i = q * 4 + v;
                    var a = i < before.Length ? before[i] : Vector3.zero;
                    var b = i < after.Length ? after[i] : Vector3.zero;
                    if ((a - b).sqrMagnitude > 0.01f)
                    {
                        differing++;
                        break;
                    }
                }
            }

            return differing;
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

            UniMobUI.RunApp(this.lifetime.Lifetime, panel, this.BuildPage);
        }

        private Widget BuildPage(BuildContext context)
        {
            // Read so the page rebuilds when the canvas scale moves, as the app's pages do through
            // the safe area and the scale.
            var device = UniMobDeviceState.Of(context);
            var scale = device?.Scale ?? 1f;
            var landscape = Screen.width > Screen.height;
            var orientation = landscape ? "Landscape" : "Portrait";

            return new PaddingBox
            {
                Padding = RectPadding.All(24),
                Child = new Column
                {
                    CrossAxisAlignment = CrossAxisAlignment.Stretch,
                    Spacing = 12,
                    Children =
                    {
                        new Row
                        {
                            Children =
                            {
                                new Flexible
                                {
                                    Child = this.Label(
                                        $"Randabschluesse versiegeln im {orientation}",
                                        maxLines: 1,
                                        wrap: false
                                    ),
                                },
                                this.Label(landscape ? "" : $"{scale:0.00}x"),
                            },
                        },
                        new Expanded
                        {
                            Child = new ScrollList
                            {
                                ScrollController = this.scrollController,
                                ItemCount = 40,
                                Spacing = 8,
                                ItemBuilder = (_, index) => this.Card(index, landscape),
                            },
                        },
                        this.Label(
                            "Die Wand wird vor dem Anstrich gespachtelt, geschliffen und grundiert; "
                                + "danach folgen zwei Anstriche in der vereinbarten Farbe."
                        ),
                    },
                },
            };
        }

        private Widget Card(int index, bool landscape)
        {
            var title =
                index % 5 == 0
                    ? $"Zimmer {index} mit einem sehr langen Namen der nicht in eine Zeile passt"
                    : $"Zimmer {index}";

            // Empty in one orientation and not in the other, so a pooled view is handed an empty
            // string after holding glyphs and vice versa.
            var measurement =
                (index + (landscape ? 0 : 1)) % 3 == 0 ? "" : $"{index * 1.5f:0.00} m";

            var description =
                index % 4 == 1
                    ? ""
                    : $"Position {index}: Bodenbelag entfernen, Untergrund pruefen und "
                        + $"Randabschluesse versiegeln ({(landscape ? "quer" : "hoch")}).";

            return new PaddingBox
            {
                Padding = RectPadding.Symmetric(16, 8),
                Child = new Column
                {
                    CrossAxisAlignment = CrossAxisAlignment.Stretch,
                    Spacing = 4,
                    Children =
                    {
                        new Row
                        {
                            Children =
                            {
                                new Flexible
                                {
                                    Child = this.Label(title, maxLines: 1, wrap: false),
                                },
                                this.Label(measurement, maxLines: 1, wrap: false),
                            },
                        },
                        this.Label(description),
                    },
                },
            };
        }

        private Widget Label(string value, int? maxLines = null, bool wrap = true)
        {
            return new Text
            {
                Value = value,
                FontSize = 24,
                MaxLines = maxLines,
                WrappingEnabled = wrap,
                OverflowMode = TextOverflowModes.Ellipsis,
                StyleName = this.styleName,
            };
        }

        /// <summary>
        ///     The name of a style in <paramref name="sheet"/> whose opening definition is not empty,
        ///     or null. Read through serialization because the sheet's style list is internal to TMP.
        /// </summary>
        private static string? FindStyleWithAnOpeningDefinition(TMP_StyleSheet? sheet)
        {
            if (sheet == null)
            {
                return null;
            }

            var styles = new SerializedObject(sheet).FindProperty("m_StyleList");
            for (var i = 0; i < styles.arraySize; i++)
            {
                var style = styles.GetArrayElementAtIndex(i);
                var opening = style.FindPropertyRelative("m_OpeningDefinition").stringValue;
                if (!string.IsNullOrEmpty(opening))
                {
                    return style.FindPropertyRelative("m_Name").stringValue;
                }
            }

            return null;
        }

        /// <summary>
        ///     Rotates the Game view and waits until the player sees the new size.
        /// </summary>
        private static IEnumerator SetResolution(int width, int height)
        {
            PlayModeWindow.SetCustomRenderingResolution(
                (uint)width,
                (uint)height,
                ProbeResolutionName
            );

            for (var i = 0; i < 30; i++)
            {
                if (Screen.width == width && Screen.height == height)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"the Game view never took the {width}x{height} resolution; the screen is "
                    + $"{Screen.width}x{Screen.height}"
            );
        }

        private static IEnumerator Settle(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                yield return null;
            }
        }

        /// <summary>
        ///     What the app's orientation watcher does: match width in landscape, lean towards
        ///     height in portrait, so the scale factor moves by more than TMP's 20% threshold.
        /// </summary>
        private sealed class MatchesOrientation : MonoBehaviour
        {
            private CanvasScaler scaler = null!;

            private void Awake()
            {
                this.scaler = this.GetComponent<CanvasScaler>();
            }

            private void Update()
            {
                var target = Screen.width > Screen.height ? 0f : 0.25f;
                if (!Mathf.Approximately(this.scaler.matchWidthOrHeight, target))
                {
                    this.scaler.matchWidthOrHeight = target;
                }
            }
        }
    }
}
#endif
