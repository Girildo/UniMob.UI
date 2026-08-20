using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// Represents a widget that constrains its child's size to a specific width and/or height.
    /// </summary>
    public class SizedBox : SingleChildLayoutWidget
    {
        public float? Width { get; init; }
        public float? Height { get; init; }

        public SizedBox() { }

        public SizedBox(Widget? child, float? width = null, float? height = null)
        {
            Width = width;
            Height = height;
            Child = child;
        }

        // Factory methods
        public override State CreateState()
        {
            return new SizedBoxState();
        }

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderConstrainedBox((IConstrainedBoxState)state);
        }

        public static SizedBox FromWidth(float width, Widget? child = null)
        {
            return new SizedBox(child, width, null);
        }

        public static SizedBox FromHeight(float height, Widget? child = null)
        {
            return new SizedBox(child, null, height);
        }

        public static SizedBox Square(float size, Widget? child = null)
        {
            return new SizedBox(child, size, size);
        }

        public static SizedBox Shrink()
        {
            return new SizedBox(null, 0, 0);
        }

        public static SizedBox Expand(Widget? child = null)
        {
            return new SizedBox(child, float.PositiveInfinity, float.PositiveInfinity);
        }
    }

    public class SizedBoxState : SingleChildLayoutState<SizedBox>, IConstrainedBoxState
    {
        public LayoutConstraints BoxConstraints =>
            LayoutConstraints.TightFor(Widget.Width, Widget.Height);
    }
}
