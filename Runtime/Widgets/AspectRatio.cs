using UniMob.UI.Internal;
using UniMob.UI.Rendering;

namespace UniMob.UI.Widgets
{
    /// <summary>
    /// A widget that attempts to size the child to a specific aspect ratio.
    /// </summary>
    public class AspectRatio : SingleChildLayoutWidget
    {
        /// <summary>
        /// The ratio of width to height (e.g. 16f / 9f). Defaults to a square.
        /// </summary>
        public float Ratio { get; init; } = 1f;

        public override State CreateState() => new AspectRatioState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderAspectRatio((AspectRatioState)state);
        }
    }

    public class AspectRatioState : SingleChildLayoutState<AspectRatio>, IAspectRatioState
    {
        public float AspectRatio => Widget.Ratio;
    }
}
