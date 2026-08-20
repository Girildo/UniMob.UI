using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// Positions a child within itself, and sizes itself around that child.
    /// </summary>
    /// <remarks>
    /// This widget is as big as possible when its dimensions are constrained and both
    /// <see cref="WidthFactor"/> and <see cref="HeightFactor"/> are null. Where a dimension is
    /// unconstrained, or its size factor is set, it matches its child on that dimension instead.
    /// <para>
    /// Whether it fills or hugs is therefore decided by what the parent handed down, and is not
    /// visible at the call site: as a flex child it is given the flex's own cross axis and fills
    /// it, while under a scroll list it is given an unbounded main axis and hugs. Set a size
    /// factor to state the choice rather than inherit it.
    /// </para>
    /// <para>
    /// Kept identical to Flutter's Align (RenderPositionedBox) on purpose, so that Flutter's own
    /// documentation answers questions about this widget:
    /// https://api.flutter.dev/flutter/widgets/Align-class.html
    /// </para>
    /// </remarks>
    public class Align : SingleChildLayoutWidget
    {
        public Alignment Alignment { get; set; } = Alignment.Center;

        /// <summary>
        /// If set, this widget's width is the child's width multiplied by this factor.
        /// </summary>
        /// <remarks>
        /// Setting it at all, whatever the value, also switches the horizontal axis from filling to
        /// hugging. That is what makes <c>WidthFactor = 1</c> the way to ask for a shrink-wrap
        /// rather than the no-op multiply it reads as. A hugging axis has no space left over, so
        /// <see cref="Alignment"/> stops moving the child horizontally once this is set.
        /// </remarks>
        public float? WidthFactor { get; set; }

        /// <summary>
        /// If set, this widget's height is the child's height multiplied by this factor.
        /// </summary>
        /// <remarks>
        /// The vertical mirror of <see cref="WidthFactor"/>, including the switch from filling to
        /// hugging and the effect that has on <see cref="Alignment"/>.
        /// </remarks>
        public float? HeightFactor { get; set; }

        public override State CreateState()
        {
            return new AlignState();
        }

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderPositionedBox((AlignState)state);
        }
    }

    internal class AlignState : SingleChildLayoutState<Align>, IPositionedBoxState
    {
        public float? WidthFactor => Widget.WidthFactor;

        public float? HeightFactor => Widget.HeightFactor;

        public Alignment Alignment => Widget.Alignment;
    }
}
