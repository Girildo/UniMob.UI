using System;
using UniMob.UI.Rendering;

namespace UniMob.UI.Widgets
{
    /// <summary>
    /// Makes its child clickable. Backed by a Unity Button component.
    /// </summary>
    /// <remarks>
    /// It paints nothing and carries no raycast target of its own, so a click lands on what the child
    /// draws: a subtree that reaches no pixels cannot be hit, whatever space it occupies. For a tap
    /// target that paints nothing -- a dismiss scrim, a hit area larger than its visual -- use
    /// <see cref="GestureDetector"/> instead; it carries an invisible raycast target for that purpose.
    /// </remarks>
    public class Clickable : SingleChildLayoutWidget
    {
        public bool Interactable { get; init; } = true;

        /// <summary>
        /// Called when the child is activated. An activation is not a pointer event: the button
        /// underneath also raises this on submit, where there is no pointer and so no position to
        /// report. A control that needs to know WHERE it was touched wants
        /// <see cref="GestureDetector"/>, which reports the point it was given.
        /// </summary>
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
