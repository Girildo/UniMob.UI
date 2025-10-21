using UniMob.UI.Internal;
using UnityEngine;
using UnityEngine.UI;

[assembly: RegisterComponentViewFactory("$$_Layout.UniMobButtonView",
    typeof(UniMob.UI.Layout.Internal.Views.UniMobButtonView))]

namespace UniMob.UI.Layout.Internal.Views
{
    [RequireComponent(typeof(RectTransform), typeof(Button))]
    internal class UniMobButtonView : SingleChildLayoutView<IUniMobButtonState>
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
            if (!HasState) return;

            State.OnClick();
        }
    }
}
