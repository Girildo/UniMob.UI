using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Layout.Internal.Views;
using UnityEngine;

namespace UniMob.UI.Layout
{
    public class Opacity : SingleChildLayoutWidget
    {
        public IAnimation<float> OpacityValue { get; init; } = new ConstAnimation<float>(1f);
        public IAnimation<Vector2> Position { get; init; } =
            new ConstAnimation<Vector2>(Vector2.zero);
        public IAnimation<Vector3> Scale { get; init; } = new ConstAnimation<Vector3>(Vector3.one);
        public IAnimation<Quaternion> Rotation { get; init; } =
            new ConstAnimation<Quaternion>(Quaternion.identity);

        public override State CreateState() => new OpacityState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderProxy((OpacityState)state);
        }
    }

    internal class OpacityState : SingleChildLayoutState<Opacity>, IOpacityState
    {
        public override WidgetViewReference View { get; } =
            WidgetViewReference.Resource("$$_Layout.Opacity");

        public IAnimation<float> OpacityValue => Widget.OpacityValue;
    }
}
