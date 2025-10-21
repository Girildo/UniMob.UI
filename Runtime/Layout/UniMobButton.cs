using System;
using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// A widget that detects click interactions.
    /// </summary>
    public class UniMobButton : SingleChildLayoutWidget
    {
        public bool Interactable { get; set; } = true;
        public Action? OnClick { get; set; }

        public override State CreateState() => new UniMobButtonState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderProxy((UniMobButtonState) state);
        }
    }

    internal interface IUniMobButtonState : ISingleChildLayoutState
    {
        bool Interactable { get; }
        void OnClick();
    }

    internal class UniMobButtonState : SingleChildLayoutState<UniMobButton>,
        IUniMobButtonState
    {
        public override WidgetViewReference View => WidgetViewReference.Resource("$$_Layout.UniMobButtonView");
        public bool Interactable => Widget.Interactable;
        
        public void OnClick()
        {
            using (Atom.NoWatch)
            {
                Widget.OnClick?.Invoke();
            }
        }
    }

}