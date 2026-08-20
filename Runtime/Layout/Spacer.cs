namespace UniMob.UI.Layout
{
    /// <summary>
    /// Represents a flexible spacer widget that can occupy space proportionally within <see cref="Row"/> and <see cref="Column"/> layouts.
    /// </summary>
    /// <remarks>The <see cref="Flex"/> property determines the proportional amount of space this spacer
    /// occupies relative to other widgets in the same layout.</remarks>
    public class Spacer : StatefulWidget
    {
        public int Flex { get; set; } = 1;

        public override State CreateState() => new SpacerState();
    }

    public class SpacerState : HocState<Spacer>
    {
        public override Widget Build(BuildContext context)
        {
            return new Expanded() { Child = SizedBox.Shrink(), Flex = Widget.Flex };
        }
    }
}
