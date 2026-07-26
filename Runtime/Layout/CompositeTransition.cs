using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Layout.Internal.Views;
using UnityEngine;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// Applies animated opacity, position, scale and rotation to a single child as a view-level effect,
    /// without changing the child's layout size. Modern-layout counterpart of the legacy
    /// <c>UniMob.UI.Widgets.CompositeTransition</c>; the two coexist and are told apart by namespace.
    /// </summary>
    public class CompositeTransition : SingleChildLayoutWidget
    {
        public IAnimation<float> Opacity { get; set; } = new ConstAnimation<float>(1f);
        public IAnimation<Vector2> Position { get; set; } = new ConstAnimation<Vector2>(Vector2.zero);
        public IAnimation<Vector3> Scale { get; set; } = new ConstAnimation<Vector3>(Vector3.one);
        public IAnimation<Quaternion> Rotation { get; set; } = new ConstAnimation<Quaternion>(Quaternion.identity);

        public override State CreateState() => new CompositeTransitionState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
            => new RenderProxy((CompositeTransitionState) state);
    }

    internal class CompositeTransitionState : SingleChildLayoutState<CompositeTransition>, ICompositeTransitionState
    {
        public override WidgetViewReference View { get; }
            = WidgetViewReference.Resource("$$_Layout.CompositeTransition");

        public IAnimation<float> Opacity => Widget.Opacity;
        public IAnimation<Vector2> Position => Widget.Position;
        public IAnimation<Vector3> Scale => Widget.Scale;
        public IAnimation<Quaternion> Rotation => Widget.Rotation;
    }
}
