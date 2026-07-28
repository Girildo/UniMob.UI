using System;
using JetBrains.Annotations;
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

        /// <summary>
        /// Which point of the CHILD lands on <see cref="Left"/>/<see cref="Top"/>. Null (the default)
        /// means its top-left, which is a plain stack placement. Setting it puts the child's centre or
        /// an edge midpoint on a computed point instead -- what anchoring an overlay to another
        /// widget needs, and which Left/Top alone cannot express because the child's size is not
        /// known to the caller.
        /// <para>
        /// Ignored on an axis positioned from <see cref="Right"/>/<see cref="Bottom"/>: such a
        /// position already accounts for the child's size.
        /// </para>
        /// <para>
        /// <b>A DELIBERATE DIVERGENCE FROM FLUTTER, not a missing port.</b> Flutter keeps this off
        /// Positioned because it can: <c>FractionalTranslation</c> and
        /// <c>CompositedTransformFollower</c> (whose <c>followerAnchor</c> is this exact concept)
        /// shift a child at PAINT time, and Flutter's hit-testing and <c>localToGlobal</c> are
        /// transform-aware, so a widget's layout box may legitimately differ from where it paints.
        /// This system has no transform render object, and geometry is read straight off
        /// <c>RectTransform.GetWorldCorners</c> -- so the same trick would make a widget report a box
        /// it does not occupy, and <c>WidgetGeometry</c>/<c>WidgetGeometryKey</c> consumers (anchoring
        /// something to an already-anchored widget, for one) would silently mis-place. Adjusting the
        /// position the PARENT assigns keeps box and paint identical, and costs nothing because
        /// RenderZStack already measures the child before it writes the position.
        /// </para>
        /// <para>
        /// Revisit if either becomes false: a paint-time transform layer is added (then
        /// FractionalTranslation is the better factoring), or a caller needs child-anchoring outside
        /// a ZStack (Positioned is only legal as a direct stack child, so this cannot serve them).
        /// </para>
        /// </summary>
        public Alignment? ChildAnchor { get; set; }

        public override State CreateState() => new PositionedState();
        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderProxy((PositionedState)state);
        }

        /// <summary>
        /// Returns a positioned widget that fills the stack by setting <see cref="Left"/>, <see cref="Right"/>, 
        /// <see cref="Top"/>, <see cref="Bottom"/> all to 0.
        /// </summary>
        public static Positioned Fill([CanBeNull]Widget child) => new (){Left = 0, Right = 0, Top = 0, Bottom = 0, Child = child};
    }

    public class PositionedState : SingleChildLayoutState<Positioned>
    {
        public override void InitState()
        {
            if(this.Widget.Left != null && this.Widget.Right != null && this.Widget.Width != null)
                throw new ArgumentException("Cannot specify all of Left, Right and Width. At most two of these properties can be specified.");
            if(this.Widget.Top != null && this.Widget.Bottom != null && this.Widget.Height != null)
                throw new ArgumentException("Cannot specify all of Top, Bottom and Height. At most two of these properties can be specified.");

        }
    }
}