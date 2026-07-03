using UniMob.UI.Layout.Internal.RenderObjects;


namespace UniMob.UI.Layout
{
    /// <summary>
    /// A widget that insets its child by the given padding.
    /// </summary>
    public class PaddingBox : SingleChildLayoutWidget
    {
        public RectPadding Padding { get; set; }

        public PaddingBox(RectPadding padding)
        {
            this.Padding = padding;
        }

        public override State CreateState() => new PaddingBoxState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderPadding((IPaddingState) state);
        }
    }


    public interface IPaddingState : ISingleChildLayoutState
    {
        RectPadding Padding { get; }
    }

    public class PaddingBoxState : SingleChildLayoutState<PaddingBox>, IPaddingState
    {
        public RectPadding Padding => Widget.Padding;
    }
}