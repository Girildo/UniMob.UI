using System;
using UniMob.UI.Rendering;

namespace UniMob.UI.Widgets
{
    /// <summary>
    /// A widget that detects click interactions. Backed by a Unity Button component.
    /// </summary>
    public class Clickable : SingleChildLayoutWidget
    {
        public bool Interactable { get; init; } = true;
        public Action? OnClick { get; init; }

        public override State CreateState() => new ClickableState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderProxy((ClickableState)state);
        }
    }

    internal class ClickableState : SingleChildLayoutState<Clickable>, IClickableState
    {
        public override WidgetViewReference View =>
            WidgetViewReference.Registered("UniMob.ClickableView");
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
