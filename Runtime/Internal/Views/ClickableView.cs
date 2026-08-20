using UniMob.UI;
using UniMob.UI.Internal;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.UI;

[assembly: RegisterComponentViewFactory(
    "UniMob.ClickableView",
    typeof(UniMob.UI.Internal.Views.ClickableView)
)]

namespace UniMob.UI.Internal.Views
{
    [RequireComponent(typeof(RectTransform), typeof(Button))]
    internal class ClickableView : SingleChildLayoutView<IClickableState>
    {
        private Button _button = null!;

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
