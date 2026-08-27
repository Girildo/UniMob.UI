using System;
using UniMob.UI.Rendering;

namespace UniMob.UI.Widgets
{
    /// <summary>
    /// A widget that insets its child by the given padding.
    /// </summary>
    public class PaddingBox : SingleChildLayoutWidget
    {
        /// <summary>
        /// The amount of space by which to inset the child.
        /// </summary>
        public RectPadding Padding { get; init; } = RectPadding.Zero;

        public override State CreateState() => new PaddingBoxState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderPadding((IPaddingState)state);
        }

        public override string? GetDiagnosticInfo()
        {
            return $"{this.Padding}";
        }
    }

    public class PaddingBoxState : SingleChildLayoutState<PaddingBox>, IPaddingState
    {
        public RectPadding Padding => Widget.Padding;
    }
}
