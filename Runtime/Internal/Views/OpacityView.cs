using UniMob.UI;
using UniMob.UI.Internal;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

[assembly: RegisterComponentViewFactory(
    "UniMob.OpacityView",
    typeof(RectTransform),
    typeof(CanvasGroup),
    typeof(UniMob.UI.Internal.Views.OpacityView)
)]

namespace UniMob.UI.Internal.Views
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
