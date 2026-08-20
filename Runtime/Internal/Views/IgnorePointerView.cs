using UniMob.UI;
using UniMob.UI.Internal;
using UniMob.UI.Internal.Views;
using UniMob.UI.Rendering;
using UnityEngine;

[assembly: RegisterComponentViewFactory(
    "UniMob.IgnorePointerView",
    typeof(RectTransform),
    typeof(CanvasGroup),
    typeof(IgnorePointerView)
)]

namespace UniMob.UI.Internal.Views
{
    internal class IgnorePointerView : SingleChildLayoutView<IIgnorePointerState>
    {
        private CanvasGroup _canvasGroup = null!;

        protected override void Awake()
        {
            base.Awake();

            _canvasGroup = GetComponent<CanvasGroup>();
        }

        protected override void Render()
        {
            base.Render();

            // blocksRaycasts, not interactable: interactable is only consulted by Selectable, so a
            // GestureDetector subtree (raw pointer handlers on a UIBehaviour) would keep firing.
            _canvasGroup.blocksRaycasts = !this.State.Ignoring;
        }
    }

    internal interface IIgnorePointerState : ISingleChildLayoutState
    {
        bool Ignoring { get; }
    }
}
