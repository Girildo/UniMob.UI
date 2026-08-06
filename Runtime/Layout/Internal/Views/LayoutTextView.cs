using System.Diagnostics;
using TMPro;
using UniMob.UI.Diagnostics;
using UniMob.UI.Internal;
using UniMob.UI.Layout.Internal.Views;
using UniMob.UI.Widgets;
using UnityEngine;

[assembly: RegisterComponentViewFactory("$$_Layout.Text",
    typeof(LayoutTextView),
    typeof(UniMobTextMeshProBehaviour))]

namespace UniMob.UI.Layout.Internal.Views
{
    [RequireComponent(typeof(UniMobTextMeshProBehaviour))]
    public class LayoutTextView : View<ITextState>
    {
        [SerializeField] private UniMobTextMeshProBehaviour text;

        private void Awake()
        {
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
            if (text == null) return;

            text.text = State.Value;
            // Sampled here rather than in Build: AnimationController.Value is an atom and Render is a
            // reactive scope, so a tick re-runs only this method and writes one colour.
            text.color = State.AnimatedColor.Value;
            text.fontSize = State.FontSize;
            text.fontWeight = State.FontWeight;
            text.textStyle = State.Style;
            text.enableWordWrapping = State.WrappingEnabled;
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
                _ => HorizontalAlignmentOptions.Left
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

            var needed = text.GetPreferredValues(State.Value, box.x, float.PositiveInfinity);
            var shortfall = needed.y - box.y;

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
    }
}