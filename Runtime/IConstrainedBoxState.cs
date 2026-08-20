using UniMob.UI.Rendering;

namespace UniMob.UI
{
    public interface IConstrainedBoxState : ISingleChildLayoutState
    {
        LayoutConstraints BoxConstraints { get; }
    }
}
