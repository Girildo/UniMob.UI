namespace UniMob.UI
{
    public interface IPositionedBoxState : ISingleChildLayoutState
    {
        Alignment Alignment { get; }
        float? WidthFactor { get; }
        float? HeightFactor { get; }
    }
}
