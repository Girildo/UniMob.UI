#nullable enable
using System;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Layout
{
    public class MeasuredRenderProxy : RenderProxy
    {
        private readonly Action<Vector2> onSize;
        private Vector2 lastReported = new(float.NaN, float.NaN);

        public MeasuredRenderProxy(ISingleChildLayoutState state, Action<Vector2> onSize)
            : base(state) => this.onSize = onSize;

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            var size = base.PerformSizing(constraints); // child's resolved size, logical space
            if (size != this.lastReported)              // only fire on actual change
            {
                this.lastReported = size;
                UnityEngine.Debug.Log("reporting size?");
                this.onSize(size);
            }
            return size;
        }
    }

    public class MeasuredBox : SingleChildLayoutWidget
    {
        public Action<Vector2>? OnSize { get; set; }
        public override State CreateState() => new MeasuredBoxState();
        public override RenderObject CreateRenderObject(BuildContext context, IState state) =>
            new MeasuredRenderProxy((MeasuredBoxState) state, ((MeasuredBoxState) state).Report);
    }

    internal class MeasuredBoxState : SingleChildLayoutState<MeasuredBox>
    {
        public Action<Vector2> Report => this.Widget.OnSize ?? (_ => { });
    }
}