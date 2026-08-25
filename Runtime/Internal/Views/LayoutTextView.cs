using System.Diagnostics;
using TMPro;
using UniMob.UI;
using UniMob.UI.Diagnostics;
using UniMob.UI.Internal;
using UniMob.UI.Internal.Views;
using UniMob.UI.Widgets;
using Unity.Profiling;
using UnityEngine;

[assembly: RegisterComponentViewFactory(
    "UniMob.LayoutTextView",
    typeof(RectTransform),
    typeof(CanvasRenderer),
    typeof(UniMobTextMeshProBehaviour),
    typeof(UniMob.UI.Internal.Views.LayoutTextView)
)]

namespace UniMob.UI.Internal.Views
{
    [RequireComponent(typeof(UniMobTextMeshProBehaviour))]
    public class LayoutTextView : View<ITextState>
    {
        [SerializeField]
        private UniMobTextMeshProBehaviour text = null!;

        protected override void Awake()
        {
            base.Awake();

            // Serialized when this view comes from a prefab, absent when it is built in source.
            if (text == null)
            {
                TryGetComponent(out text);
            }

            // Registered once per component, not once per activation: the callback list is append-only
            // and is never cleared, so re-registering would grow it for the life of the view.
            AddActivationCallback(DiscardInheritedGeometry);
        }

        /// <summary>
        ///     Hands this view to its new state carrying none of the previous one's glyphs.
        /// </summary>
        /// <remarks>
        ///     Works around a TMP defect that otherwise resurrects discarded text on a window resize or a
        ///     device rotation, permanently.
        ///     <para>
        ///         TMP ends its empty-text generation in <c>ClearMesh()</c>, which is only
        ///         <c>canvasRenderer.SetMesh(null)</c> -- the geometry itself survives in the mesh info.
        ///         Anything that later measures a non-empty string against this component repopulates the
        ///         character buffer guarding <c>InternalUpdate</c> without disturbing the character count,
        ///         the dirty flag or the mesh. A lossy-scale change past TMP's 20% threshold then reaches
        ///         <c>UpdateSDFScale</c>, whose last act is an unconditional
        ///         <c>canvasRenderer.SetMesh(m_mesh)</c>, and the discarded glyphs are on screen again with
        ///         nothing left to describe the fault: character count already 0, dirty flag already
        ///         false, so no rebuild is ever scheduled. Only a large resize crosses that threshold,
        ///         which is why it tracks maximising and rotating rather than dragging an edge.
        ///     </para>
        ///     <para>
        ///         The string is reset alongside the geometry, and that pairing is load-bearing:
        ///         <see cref="Render" />'s assignment is a no-op when the incoming value equals the
        ///         outgoing one, so discarding geometry on its own would leave such a view permanently
        ///         blank. Emptying the text first guarantees the assignment that follows always differs.
        ///     </para>
        ///     <para>
        ///         Cleared through TMP's own <c>ClearMeshInfo</c> rather than <c>Mesh.Clear()</c>, which
        ///         is a trap: dropping the vertices leaves the UV array longer than them, and the stray
        ///         upload writes UVs before it uploads, so every resize would throw "Mesh.uv is out of
        ///         bounds" instead. TMP zeroes its vertices in place, keeping the arrays the same length.
        ///     </para>
        /// </remarks>
        private void DiscardInheritedGeometry()
        {
            if (text == null)
            {
                return;
            }

            text.text = string.Empty;
            text.textInfo?.ClearMeshInfo(true);
        }

        protected override void Render()
        {
            if (text == null)
                return;

            // Assigning only marks TMP dirty. The mesh rebuild it provokes is deferred to the canvas
            // update later in the frame, where TMP's own markers already account for it -- so there is
            // deliberately no marker here to imply otherwise.
            text.text = State.Value;

            // Sampled here rather than in Build: AnimationController.Value is an atom and Render is a
            // reactive scope, so a tick re-runs only this method and writes one colour.
            text.color = State.AnimatedColor.Value;
            text.fontSize = State.FontSize;
            text.fontWeight = State.FontWeight;

            // Guarded, unlike every other property here, because TMP's textStyle setter is the one that
            // does not guard itself: it sets havePropertiesChanged and calls SetVerticesDirty and
            // SetLayoutDirty unconditionally, so assigning the style a text already has still schedules
            // a full regeneration. Render re-runs on every layout pass, so writing it straight through
            // regenerates every visible label every frame -- the whole cost of a scroll.
            if (!ReferenceEquals(text.textStyle, State.Style))
            {
                text.textStyle = State.Style;
            }
            text.textWrappingMode = State.WrappingEnabled
                ? TextWrappingModes.Normal
                : TextWrappingModes.NoWrap;
            text.overflowMode = State.OverflowMode;
            text.maxVisibleLines = State.MaxLines;

            text.horizontalAlignment = State.HorizontalTextAlign switch
            {
                HorizontalTextAlignment.Left => HorizontalAlignmentOptions.Left,
                HorizontalTextAlignment.Center => HorizontalAlignmentOptions.Center,
                HorizontalTextAlignment.Right => HorizontalAlignmentOptions.Right,
                HorizontalTextAlignment.Justified => HorizontalAlignmentOptions.Justified,
                HorizontalTextAlignment.Flush => HorizontalAlignmentOptions.Flush,
                HorizontalTextAlignment.Geometry => HorizontalAlignmentOptions.Geometry,
                _ => HorizontalAlignmentOptions.Left,
            };

            text.verticalAlignment = VerticalAlignmentOptions.Middle;

            NoteTextThatDoesNotFit();
        }

        /// <summary>
        ///     Whether the text that will actually be drawn fits the box layout gave it.
        /// </summary>
        /// <remarks>
        ///     Asked of the live component rather than of the shared measurer, and that is the whole
        ///     point. Everything that could make the two disagree -- the resolved style and font asset,
        ///     margins, wrapping mode, auto-sizing, whatever the prefab carries -- is already applied to
        ///     this component by the time Render reaches here. A check written on the layout side cannot
        ///     see such a divergence, because both halves of that comparison would come from the same
        ///     measurement: layout would be comparing its answer against itself and always agreeing.
        ///     <para>
        ///         Paint is therefore the only place this fault is decidable, exactly as it is for an
        ///         infinite size reaching a RectTransform.
        ///     </para>
        ///     <para>
        ///         Skipped where the author capped the line count. GetPreferredValues reports the natural
        ///         size and ignores maxVisibleLines, so a deliberately clamped text would otherwise
        ///         report the height of the lines it was told to drop.
        ///     </para>
        ///     <para>
        ///         Its own dedup rather than the render object's latch, for the reason
        ///         <see cref="MultiChildLayoutView"/> gives: that latch is scoped to a layout pass, and
        ///         paint runs many times without one in between. Re-arms when the text fits again.
        ///     </para>
        /// </remarks>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNIMOB_UI_FORCE_DIAGNOSTICS")]
        private void NoteTextThatDoesNotFit()
        {
            // An auto-sizing text fits its box by construction -- that is what auto-sizing is -- so
            // there is no divergence to find. Asking it for a preferred height against an unbounded
            // one is the same circular question that made the icon prefabs measure at their 32767
            // font cap: it answers with how tall it could grow, not with how tall it will be drawn.
            if (text.enableAutoSizing)
            {
                return;
            }

            if (State.MaxLines != int.MaxValue)
            {
                return;
            }

            var box = State.RenderObject?.PeekSize() ?? Vector2.zero;
            if (!float.IsFinite(box.x) || !float.IsFinite(box.y) || box.x <= 0f)
            {
                return;
            }

            // Nothing the answer depends on has moved, so the answer cannot have moved either.
            // Load-bearing for frame cost, not just tidiness: Render runs inside a reactive scope that
            // reads WatchLayout, so it re-runs on every layout pass, and GetPreferredValues re-parses
            // the whole string into the component's buffers on every call with no cache of its own.
            // Without this guard a scrolling list pays a full text-processing pass per visible label
            // per frame for a diagnostic that had already decided.
            if (
                _lastCheckedValue == State.Value
                && _lastCheckedBox == box
                && _lastCheckedFontSize == State.FontSize
                && ReferenceEquals(_lastCheckedStyle, State.Style)
            )
            {
                return;
            }

            _lastCheckedValue = State.Value;
            _lastCheckedBox = box;
            _lastCheckedFontSize = State.FontSize;
            _lastCheckedStyle = State.Style;

            float shortfall;
            using (TextFitDiagnosticMarker.Auto())
            {
                shortfall =
                    text.GetPreferredValues(State.Value, box.x, float.PositiveInfinity).y - box.y;
            }

            if (shortfall <= LayoutConstants.OverflowTolerance)
            {
                _reportedNotFitting = false;
                return;
            }

            if (_reportedNotFitting)
            {
                return;
            }

            _reportedNotFitting = true;

            UniMobDiagnostics.Report(
                new LayoutIssue(
                    LayoutIssueCode.ContentOverflow,
                    State,
                    LayoutAxes.Vertical,
                    LayoutMeasuredLessThanIsDrawn,
                    size: box,
                    amount: shortfall
                )
            );
        }

        private const string LayoutMeasuredLessThanIsDrawn =
            "The text drawn here needs more height than layout measured for it, at the same width. "
            + "Give it more room, or find why the measurement and the renderer disagree.";

        private bool _reportedNotFitting;

        private string? _lastCheckedValue;
        private Vector2 _lastCheckedBox;
        private int _lastCheckedFontSize;
        private TMP_Style? _lastCheckedStyle;

        // Named so a Profiler capture can tell this apart from the deferred canvas rebuild: TMP's own
        // markers nest inside this one when the cost is ours, and sit outside it when the cost is the
        // rebuild. Measurement is marked separately, in RenderText.
        private static readonly ProfilerMarker TextFitDiagnosticMarker = new(
            "UniMob.Text.FitDiagnostic"
        );
    }
}
