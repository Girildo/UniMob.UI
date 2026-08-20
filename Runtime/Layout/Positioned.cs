using System;
using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// A widget that controls where a child of a ZStack is positioned.
    /// This is a signal widget and does not create its own RenderObject.
    /// </summary>
    public class Positioned : SingleChildLayoutWidget
    {
        public float? Left { get; set; }
        public float? Top { get; set; }
        public float? Right { get; set; }
        public float? Bottom { get; set; }
        public float? Width { get; set; }
        public float? Height { get; set; }

        public override State CreateState() => new PositionedState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderProxy((PositionedState)state);
        }

        /// <summary>
        /// Returns a positioned widget that fills the stack by setting <see cref="Left"/>, <see cref="Right"/>,
        /// <see cref="Top"/>, <see cref="Bottom"/> all to 0.
        /// </summary>
        public static Positioned Fill(Widget? child) =>
            new()
            {
                Left = 0,
                Right = 0,
                Top = 0,
                Bottom = 0,
                Child = child,
            };
    }

    public class PositionedState : SingleChildLayoutState<Positioned>
    {
        public override void InitState()
        {
            if (this.Widget.Left != null && this.Widget.Right != null && this.Widget.Width != null)
                throw new ArgumentException(
                    "Cannot specify all of Left, Right and Width. At most two of these properties can be specified."
                );
            if (this.Widget.Top != null && this.Widget.Bottom != null && this.Widget.Height != null)
                throw new ArgumentException(
                    "Cannot specify all of Top, Bottom and Height. At most two of these properties can be specified."
                );
        }
    }
}
