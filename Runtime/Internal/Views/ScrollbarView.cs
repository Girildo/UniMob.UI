using UniMob.UI;
using UniMob.UI.Internal;
using UniMob.UI.Internal.Views;
using UnityEngine;

[assembly: RegisterComponentViewFactory(
    "UniMob.ScrollbarView",
    typeof(RectTransform),
    typeof(CanvasGroup),
    typeof(InvisibleRaycastTarget),
    typeof(GestureDetectorTapReceiver),
    typeof(ScrollbarView)
)]

namespace UniMob.UI.Internal.Views
{
    internal class ScrollbarView : SingleChildLayoutView<IScrollbarState>
    {
        private CanvasGroup _canvasGroup = null!;
        private InvisibleRaycastTarget _raycastTarget = null!;
        private GestureDetectorTapReceiver _tapReceiver = null!;

        protected override void Awake()
        {
            base.Awake();

            _canvasGroup = GetComponent<CanvasGroup>();
            _raycastTarget = GetComponent<InvisibleRaycastTarget>();
            _tapReceiver = GetComponent<GestureDetectorTapReceiver>();
        }

        protected override void Render()
        {
            base.Render();

            _canvasGroup.alpha = State.Opacity.Value;

            // blocksRaycasts, not interactable: interactable is only consulted by Selectable, so the
            // thumb's own gesture receivers would keep firing through a hidden bar.
            _canvasGroup.blocksRaycasts = State.BlocksPointer;

            _tapReceiver.OnTap = State.OnTrackTap;

            // The track is a hit target only while a tap there does something; the thumb carries its
            // own. Leaving it on would eat every drag aimed at the list underneath.
            _raycastTarget.raycastTarget = State.OnTrackTap != null;
        }
    }
}
