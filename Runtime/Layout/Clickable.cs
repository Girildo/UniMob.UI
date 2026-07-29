#nullable enable
using System;
using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// A widget that detects click interactions. Backed by a Unity Button component.
    /// </summary>
    public class Clickable : SingleChildLayoutWidget
    {
        public bool Interactable { get; set; } = true;
        public Action? OnClick { get; set; }

        public override State CreateState() => new ClickableState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderProxy((ClickableState) state);
        }
    }

    internal interface IClickableState : ISingleChildLayoutState
    {
        bool Interactable { get; }
        void OnClick();
    }

    internal class ClickableState : SingleChildLayoutState<Clickable>,
        IClickableState
    {
        public override WidgetViewReference View => WidgetViewReference.Resource("$$_Layout.ClickableView");
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