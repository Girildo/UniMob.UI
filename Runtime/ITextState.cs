using TMPro;
using UnityEngine;

namespace UniMob.UI
{
    public interface ITextState : IViewState
    {
        string Value { get; }
        Color Color { get; }

        /// <summary>The colour to paint, as an animation; a constant one when nothing animates it.</summary>
        IAnimation<Color> AnimatedColor { get; }

        int FontSize { get; }

        TMP_Style Style { get; }

        FontWeight FontWeight { get; }
        HorizontalTextAlignment HorizontalTextAlign { get; }

        bool WrappingEnabled { get; }
        TextOverflowModes OverflowMode { get; }

        int MaxLines { get; }
    }
}
