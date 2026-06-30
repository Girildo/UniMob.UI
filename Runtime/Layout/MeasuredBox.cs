#nullable enable
using System;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// A widget that measures the size of its child and reports it via a callback.
    /// </summary>
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