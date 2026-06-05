using UniMob.UI.Internal;
using UniMob.UI.Layout.Internal.Views;
using UnityEngine;

[assembly: RegisterComponentViewFactory("$$_Layout.IgnorePointer",
    typeof(RectTransform), typeof(CanvasGroup), typeof(IgnorePointerView))]

namespace UniMob.UI.Layout.Internal.Views
{
    internal class IgnorePointerView : SingleChildLayoutView<IIgnorePointerState>
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

            _canvasGroup.interactable = !this.State.Ignoring;
        }
    }

    internal interface IIgnorePointerState : ISingleChildLayoutState
    {
        bool Ignoring { get; }
    }
}