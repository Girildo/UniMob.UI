using JetBrains.Annotations;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// A widget that controls where a child of a ZStack is positioned.
    /// This is a signal widget and does not create its own RenderObject.
    /// </summary>
    public class Positioned : StatefulWidget
    {
        public Widget Child { get; set; }

        public float? Left { get; set; }
        public float? Top { get; set; }
        public float? Right { get; set; }
        public float? Bottom { get; set; }
        public float? Width { get; set; }
        public float? Height { get; set; }

        public override State CreateState() => new PositionedState();
    }

    public class PositionedState : HocState<Positioned>
    {
        public override Widget Build(BuildContext context)
        {
            return Widget.Child;
        }
    }
}