using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Layout.Internal
{
    public class RenderImage : LeafRenderObject
    {
        private readonly IImageState _state;

        private Texture? Texture => _state.Texture;
        private ImageFit Fit => _state.Fit;

        public RenderImage(IImageState state)
            : base(state)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            if (Texture == null)
            {
                return constraints.Constrain(Vector2.zero);
            }

            Vector2 intrinsicSize = new Vector2(Texture.width, Texture.height);

            // If the constraints dictate an exact size, we must obey.
            if (constraints.IsTight)
            {
                return constraints.Constrain(intrinsicSize);
            }

            // Calculate the ideal layout size based on the Fit mode
            Vector2 desiredSize = intrinsicSize;

            switch (Fit)
            {
                case ImageFit.Fill:
                case ImageFit.Cover:
                    // Wants to be as large as the parent allows
                    desiredSize = new Vector2(constraints.MaxWidth, constraints.MaxHeight);
                    break;
                case ImageFit.Contain:
                case ImageFit.ScaleDown:
                    // We calculate the scaled size that perfectly fits inside the constraints
                    desiredSize = CalculateScaleToFit(intrinsicSize, constraints);
                    if (Fit == ImageFit.ScaleDown)
                    {
                        desiredSize.x = Mathf.Min(desiredSize.x, intrinsicSize.x);
                        desiredSize.y = Mathf.Min(desiredSize.y, intrinsicSize.y);
                    }
                    break;
                case ImageFit.FitWidth:
                    desiredSize.x = constraints.MaxWidth;
                    desiredSize.y = desiredSize.x * (intrinsicSize.y / intrinsicSize.x);
                    break;
                case ImageFit.FitHeight:
                    desiredSize.y = constraints.MaxHeight;
                    desiredSize.x = desiredSize.y * (intrinsicSize.x / intrinsicSize.y);
                    break;
                case ImageFit.None:
                    desiredSize = intrinsicSize;
                    break;
            }

            // FitWidth and FitHeight pin one axis and derive the other from the texture's ratio, so
            // the derived axis can land outside the box. ImageFit.None states that the texture keeps
            // its own size whatever the box says, so exceeding it is that mode working rather than
            // failing -- handing the facade the largest allowed size excuses it without leaving an if
            // block that is empty in a release build.
            ReportContentOverflow(
                constraints,
                Fit == ImageFit.None ? constraints.Largest : desiredSize,
                ChooseAFitThatFits
            );

            return constraints.Constrain(desiredSize);
        }

        private const string ChooseAFitThatFits =
            "This fit mode derives one axis from the other, and the result is larger than the box. "
            + "Use Contain to fit both axes, or give the image a box matching its ratio.";

        private Vector2 CalculateScaleToFit(Vector2 src, LayoutConstraints constraints)
        {
            float widthRatio = constraints.MaxWidth / src.x;
            float heightRatio = constraints.MaxHeight / src.y;
            float minRatio = Mathf.Min(widthRatio, heightRatio);

            // If parent gives infinite space, just use the native size
            if (float.IsPositiveInfinity(minRatio))
                return src;

            return src * minRatio;
        }

        protected override float ComputeIntrinsicWidth(float height)
        {
            if (Texture == null)
                return 0;
            if (float.IsPositiveInfinity(height) || Fit == ImageFit.None)
                return Texture.width;

            return height * ((float)Texture.width / Texture.height);
        }

        protected override float ComputeIntrinsicHeight(float width)
        {
            if (Texture == null)
                return 0;
            if (float.IsPositiveInfinity(width) || Fit == ImageFit.None)
                return Texture.height;

            return width * ((float)Texture.height / Texture.width);
        }
    }
}
