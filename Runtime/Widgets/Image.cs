using UniMob.UI.Internal;
using UniMob.UI.Rendering;
using UnityEngine;

namespace UniMob.UI.Widgets
{
    public enum ImageFit
    {
        /// <summary>
        /// Fill the target box by distorting the image's aspect ratio.
        /// </summary>
        Fill,

        /// <summary>
        /// As large as possible while maintaining aspect ratio and staying inside the box.
        /// </summary>
        Contain,

        /// <summary>
        /// Fill the target box completely, maintaining aspect ratio. Will crop the image if necessary.
        /// </summary>
        Cover,

        /// <summary>
        /// Make sure the full width of the image is shown. May crop vertically or leave empty vertical space.
        /// </summary>
        FitWidth,

        /// <summary>
        /// Make sure the full height of the image is shown. May crop horizontally or leave empty horizontal space.
        /// </summary>
        FitHeight,

        /// <summary>
        /// Do not scale the image. Centers it inside the box.
        /// </summary>
        None,

        /// <summary>
        /// Scale down to fit inside the box, but do not scale up if the image is smaller than the box.
        /// </summary>
        ScaleDown,
    }

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

    public interface IImageState : IViewState
    {
        Texture? Texture { get; }
        Color Color { get; }
        ImageFit Fit { get; }
        Alignment Alignment { get; }
    }

    public class ImageState : ViewState<Image>, IImageState
    {
        public Texture? Texture => Widget.Texture;
        public Color Color => Widget.Color;
        public ImageFit Fit => Widget.Fit;
        public Alignment Alignment => Widget.Alignment;
        public override WidgetViewReference View =>
            WidgetViewReference.Resource("$$_Layout.ImageView");
    }
}
