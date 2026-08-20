using UniMob.UI.Internal;
using UnityEngine;
using UnityEngine.UI;

[assembly: RegisterComponentViewFactory(
    "$$_Layout.ClickableView",
    typeof(UniMob.UI.Layout.Internal.Views.ClickableView)
)]

namespace UniMob.UI.Layout.Internal.Views
{
    [RequireComponent(typeof(RectTransform), typeof(Button))]
    internal class ClickableView : SingleChildLayoutView<IClickableState>
    {
        private Button _button;

        protected override void Awake()
        {
            base.Awake();

            _button = GetComponent<Button>();
            _button.onClick.AddListener(HandleClick);
        }

        protected override void Render()
        {
            base.Render();

            _button.interactable = State.Interactable;
        }

        private void HandleClick()
        {
            if (!HasState)
                return;

            State.OnClick();
        }
    }
}
