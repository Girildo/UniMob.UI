using UniMob.UI.Internal;
using UniMob.UI.Rendering;
using UnityEngine;

[assembly: RegisterComponentViewFactory(
    "$$_Layout.CompositeTransition",
    typeof(RectTransform),
    typeof(CanvasGroup),
    typeof(UniMob.UI.Internal.Views.CompositeTransitionView)
)]

namespace UniMob.UI.Internal.Views
{
    internal class CompositeTransitionView : SingleChildLayoutView<ICompositeTransitionState>
    {
        private CanvasGroup _canvasGroup;

        protected override void Awake()
        {
            base.Awake();

            _canvasGroup = GetComponent<CanvasGroup>();
        }

        protected override void Render()
        {
            // base.Render() lays out and top-left-positions the child; the transform below is an additive
            // view-level effect on top of that (position as an offset so the base's pivot placement is kept).
            base.Render();

            _canvasGroup.alpha = State.Opacity.Value;

            var childTransform = ChildView.rectTransform;
            childTransform.localScale = State.Scale.Value;
            childTransform.anchoredPosition += Vector2.Scale(
                State.Position.Value,
                childTransform.rect.size
            );
            childTransform.localRotation = State.Rotation.Value;
        }
    }

    internal interface ICompositeTransitionState : ISingleChildLayoutState
    {
        IAnimation<float> Opacity { get; }
        IAnimation<Vector2> Position { get; }
        IAnimation<Vector3> Scale { get; }
        IAnimation<Quaternion> Rotation { get; }
    }
}
