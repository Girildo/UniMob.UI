using UnityEngine;

namespace UniMob.UI
{
    internal interface IColoredImageBoxState : ISingleChildLayoutState
    {
        Color BackgroundColor { get; }
        Sprite? BackgroundImage { get; }
    }
}
