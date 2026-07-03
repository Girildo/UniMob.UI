using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Widgets;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// A widget that sizes its child to the child's maximum intrinsic width.
    /// </summary>
    public class IntrinsicWidth : IntrinsicSize
    {
        public override Axis Axis => Axis.Horizontal;
    }

    /// <summary>
    /// A widget that sizes its child to the child's maximum intrinsic height.
    /// </summary>
    public class IntrinsicHeight : IntrinsicSize
    {
        public override Axis Axis => Axis.Vertical;
    }

    public abstract class IntrinsicSize : SingleChildLayoutWidget
    {
        public abstract Axis Axis { get; }

        public override State CreateState() => new IntrinsicSizeState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderIntrinsicSize((IntrinsicSizeState) state);
        }
    }

    public interface IIntrinsicSizeState : ISingleChildLayoutState
    {
        Axis Axis { get; }
    }

    public class IntrinsicSizeState : SingleChildLayoutState<IntrinsicSize>, IIntrinsicSizeState
    {
        public Axis Axis => this.Widget.Axis;
    }
}