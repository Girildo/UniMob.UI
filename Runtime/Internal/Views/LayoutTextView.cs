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
        }

        protected override void Render()
        {
            if (text == null)
                return;

            // Assigning only marks TMP dirty; RegenerateNow below is what draws it.
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

            RegenerateNow();

            NoteTextThatDoesNotFit();
        }

        /// <summary>
        ///     Leaves this text drawn with the values above, rather than waiting to be asked.
        /// </summary>
        /// <remarks>
        ///     TMP draws only from <c>OnPreRenderCanvas</c>, and reaches it only if it is registered
        ///     with <c>CanvasUpdateRegistry</c> -- a registration <c>SetVerticesDirty</c> refuses
        ///     without a word while a graphic rebuild is in flight, or while the component is not
        ///     active-and-enabled. It has raised its dirty flag by then, and a raised flag is the one
        ///     state TMP's per-frame <c>InternalUpdate</c> will not act on, so a refused registration
        ///     is permanent: the component keeps the geometry it happens to be holding for the rest of
        ///     its life. That is how the same text came back after a resize, and how it went missing
        ///     after a rotation once the old workaround had emptied the mesh first.
        ///     <para>
        ///         Generating here rather than repairing it later makes the registration stop mattering
        ///         at all: every render leaves the mesh matching the string with both flags down, so
        ///         there is no missed rebuild to leave a blank. The queued rebuild still runs later in
        ///         the frame and finds nothing to do. What an emptied text leaves in its meshes is the
        ///         component's own concern, in <see cref="UniMobTextMeshProBehaviour.ClearMesh" />.
        ///     </para>
        ///     <para>
        ///         Asking the composite <see cref="UniMobTextMeshProBehaviour.WantsRegeneration" />, not
        ///         <c>havePropertiesChanged</c>: a resize dirties a text through
        ///         <c>OnRectTransformDimensionsChange</c>, which raises only the layout half. That is
        ///         the half a rotation moves, and the parent writes this view's rect before rendering
        ///         it, so the flag is already up by the time this runs.
        ///     </para>
        ///     <para>
        ///         It is also the guard that keeps this cheap. Render re-runs on every layout pass, and
        ///         a pass that changed nothing leaves both flags down, so only the labels that actually
        ///         moved or changed pay anything. Before the diagnostic below, so a release player draws
        ///         exactly what the Editor draws.
        ///     </para>
        /// </remarks>
        private void RegenerateNow()
        {
            if (text.WantsRegeneration)
            {
                text.ForceMeshUpdate();
            }
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
