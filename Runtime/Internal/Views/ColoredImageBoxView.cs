using UniMob.UI;
using UniMob.UI.Internal;
using UniMob.UI.Widgets;
using UnityEngine;

[assembly: RegisterComponentViewFactory(
    "UniMob.ColoredImageBoxView",
    typeof(UniMob.UI.Internal.Views.ColoredImageBoxView)
)]

namespace UniMob.UI.Internal.Views
{
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image))]
    internal class ColoredImageBoxView : SingleChildLayoutView<IColoredImageBoxState>
    {
        private UnityEngine.UI.Image _backgroundImage;

        protected override void Awake()
        {
            base.Awake();
            TryGetComponent(out _backgroundImage);
        }

        protected override void Render()
        {
            base.Render();

            if (_backgroundImage == null)
                return;

            // Image.sprite is unannotated, but a null sprite is how the component draws its plain
            // quad, which is exactly the no-image case.
            _backgroundImage.sprite = State.BackgroundImage!;
            _backgroundImage.color = State.BackgroundColor;
        }
    }
}
