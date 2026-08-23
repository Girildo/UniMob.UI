using UniMob.UI.Internal;
using UniMob.UI.Rendering;
using UnityEngine;

namespace UniMob.UI.Widgets
{
    public class Image : StatefulWidget
    {
        /// <summary>
        /// The texture to display in the image widget. If null, no image will be displayed.
        /// </summary>
        public Texture? Texture { get; init; }

        /// <summary>
        /// The tint color to apply to the texture. This color is multiplied with the texture's original color, allowing for tinting effects.
        /// </summary>
        public Color Color { get; init; } = Color.white;

        /// <summary>
        /// The fit mode for the image, determining how the texture is scaled and positioned within the widget's bounds.
        /// The default value is <see cref="ImageFit.Contain"/>, which scales the image to fit within the bounds while preserving its aspect ratio.
        /// </summary>
        /// <remarks>
        /// See <see cref="ImageFit"/> for available fit modes such as Contain, Cover, Fill, etc.
        /// </remarks>
        public ImageFit Fit { get; init; } = ImageFit.Contain;

        /// <summary>
        /// The alignment of the image within the widget's bounds. This determines how the image is positioned when it does not fill the entire area.
        /// The default value is <see cref="Alignment.Center"/>, which centers the image within the widget's bounds.
        /// </summary>
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
