#nullable enable
using UnityEngine;
using UniMob.UI.Layout.Internal;
using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI.Layout
{
   public enum ImageFit
    {
        /// Fill the target box by distorting the image's aspect ratio.
        Fill,
        /// As large as possible while maintaining aspect ratio and staying inside the box.
        Contain,
        /// Fill the target box completely, maintaining aspect ratio. Will crop the image if necessary.
        Cover,
        /// Make sure the full width of the image is shown. May crop vertically or leave empty vertical space.
        FitWidth,
        /// Make sure the full height of the image is shown. May crop horizontally or leave empty horizontal space.
        FitHeight,
        /// Do not scale the image. Centers it inside the box.
        None,
        /// Scale down to fit inside the box, but do not scale up if the image is smaller than the box.
        ScaleDown
    }

    public class Image : StatefulWidget
    {
        public Texture? Texture { get; set; }
        public Color Color { get; set; } = Color.white;
        public ImageFit Fit { get; set; } = ImageFit.Contain;
        public Alignment Alignment { get; set; } = Alignment.Center;

        public override State CreateState() => new ImageState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderImage((ImageState)state);
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

        public override WidgetViewReference View => WidgetViewReference.Resource("$$_Layout.ImageView");
    }
}