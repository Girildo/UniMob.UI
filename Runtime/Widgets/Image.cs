using UniMob.UI.Internal;
using UniMob.UI.Rendering;
using UnityEngine;

namespace UniMob.UI.Widgets
{
    public class Image : StatefulWidget
    {
        public Texture? Texture { get; init; }
        public Color Color { get; init; } = Color.white;
        public ImageFit Fit { get; init; } = ImageFit.Contain;
        public Alignment Alignment { get; init; } = Alignment.Center;

        public override State CreateState() => new ImageState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderImage((ImageState)state);
        }

        public override string? GetDiagnosticInfo()
        {
            // Unity's != null, never ?., which sees a live CLR reference to a destroyed texture and
            // throws on .name. A texture built at runtime is usually unnamed, and "" is not a label.
            var texture = this.Texture;
            return texture != null && !string.IsNullOrEmpty(texture.name) ? texture.name : null;
        }
    }

    public class ImageState : ViewState<Image>, IImageState
    {
        public Texture? Texture => Widget.Texture;
        public Color Color => Widget.Color;
        public ImageFit Fit => Widget.Fit;
        public Alignment Alignment => Widget.Alignment;
        public override WidgetViewReference View =>
            WidgetViewReference.Registered("UniMob.ImageView");
    }
}
