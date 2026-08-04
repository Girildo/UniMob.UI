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