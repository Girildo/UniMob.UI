using UnityEngine;

namespace UniMob.UI
{
    public interface IImageState : IViewState
    {
        Texture? Texture { get; }
        Color Color { get; }
        ImageFit Fit { get; }
        Alignment Alignment { get; }
    }
}
