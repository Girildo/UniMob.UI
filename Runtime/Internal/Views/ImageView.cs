using UniMob.UI;
using UniMob.UI.Internal;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.UI;

[assembly: RegisterComponentViewFactory(
    "$$_Layout.ImageView",
    typeof(RectTransform),
    typeof(UniMob.UI.Internal.Views.ImageView)
)]

namespace UniMob.UI.Internal.Views
{
    [RequireComponent(typeof(RawImage))]
    public class ImageView : View<IImageState>
    {
        private RawImage? _rawImage;

        protected override void Activate()
        {
            base.Activate();
            if (_rawImage == null)
            {
                _rawImage = GetComponent<RawImage>();
            }
        }

        protected override void Render()
        {
            if (_rawImage == null)
                return;
            if (State.Texture == null)
                return;

            _rawImage.color = State.Color;
            _rawImage.texture = State.Texture;

            // Sizing: The RenderImage already calculated our RectTransform size.
            // Painting: Now we calculate the UVs to map the texture inside this RectTransform.
            Rect rect = ((RectTransform)transform).rect;
            if (rect.width <= 0 || rect.height <= 0)
                return;

            Vector2 texSize = new Vector2(State.Texture.width, State.Texture.height);
            _rawImage.uvRect = CalculateUVRect(rect.size, texSize, State.Fit, State.Alignment);
        }

        /// <summary>
        /// Calculate the UVs to apply the fitting mode (cover, contain, etc) and alignment to the RawImage.
        /// </summary>
        private Rect CalculateUVRect(
            Vector2 boxSize,
            Vector2 texSize,
            ImageFit fit,
            Alignment alignment
        )
        {
            if (fit == ImageFit.Fill)
            {
                return new Rect(0, 0, 1, 1);
            }

            float boxAspect = boxSize.x / boxSize.y;
            float texAspect = texSize.x / texSize.y;

            float uvWidth = 1f;
            float uvHeight = 1f;

            bool isBoxWider = boxAspect > texAspect;

            switch (fit)
            {
                case ImageFit.Cover:
                    // To cover, we must crop the axis that overflows
                    if (isBoxWider)
                    {
                        // Crop vertical
                        uvHeight = texAspect / boxAspect;
                    }
                    else
                    {
                        // Crop horizontal
                        uvWidth = boxAspect / texAspect;
                    }
                    break;

                case ImageFit.Contain:
                case ImageFit.ScaleDown:
                    // Contain actually shrinks the mesh visually, so UVs stay 0..1.
                    // However, in our architecture, the RenderImage ALREADY shrunk the boxSize
                    // to perfectly match the aspect ratio during PerformSizing.
                    // So if it's Contain, boxAspect should perfectly equal texAspect, meaning uv is 1,1.
                    return new Rect(0, 0, 1, 1);

                case ImageFit.None:
                    // Unscaled. We show a 1:1 pixel mapping.
                    uvWidth = boxSize.x / texSize.x;
                    uvHeight = boxSize.y / texSize.y;
                    break;

                case ImageFit.FitWidth:
                    uvHeight = texAspect / boxAspect;
                    break;

                case ImageFit.FitHeight:
                    uvWidth = boxAspect / texAspect;
                    break;
            }

            // Convert UniMob Alignment (-1 to 1) to UV offset (0 to 1)
            // Example: Alignment.Left (-1) -> 0. Alignment.Center (0) -> 0.5.
            float alignU = (alignment.X + 1f) / 2f;
            float alignV = (alignment.Y + 1f) / 2f;

            // Calculate the bottom-left coordinate of the UV rect based on alignment
            float uvX = (1f - uvWidth) * alignU;
            float uvY = (1f - uvHeight) * alignV;

            return new Rect(uvX, uvY, uvWidth, uvHeight);
        }
    }
}
