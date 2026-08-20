using UniMob.UI.Internal;
using UniMob.UI.Widgets;
using UnityEngine;

[assembly: RegisterComponentViewFactory(
    "$$_Layout.Opacity",
    typeof(RectTransform),
    typeof(CanvasGroup),
    typeof(UniMob.UI.Layout.Internal.Views.OpacityView)
)]

namespace UniMob.UI.Layout.Internal.Views
{
    internal class OpacityView : SingleChildLayoutView<IOpacityState>
    {
        private CanvasGroup _canvasGroup;

        protected override void Awake()
        {
            base.Awake();

            _canvasGroup = GetComponent<CanvasGroup>();
        }

        protected override void Render()
        {
            base.Render();

            _canvasGroup.alpha = State.OpacityValue.Value;

            var childTransform = ChildView.rectTransform;
        }
    }

    internal interface IOpacityState : ISingleChildLayoutState
    {
        IAnimation<float> OpacityValue { get; }
    }
}
