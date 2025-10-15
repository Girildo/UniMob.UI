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

    // The State now implements the contract for RenderAlign (IAlignState)
    // and the contract for its View (IUniMobButtonState).
    internal class UniMobButtonState : SingleChildLayoutState<UniMobButton>,
        Widgets.IUniMobButtonState
    {

        
        

        // --- IUniMobButtonState Implementation (for the View) ---
        public override WidgetViewReference View => WidgetViewReference.Resource("UniMob.Button");
        public bool Interactable => Widget.Interactable;

        Alignment Widgets.ISingleChildLayoutState.Alignment => Alignment.TopLeft; // Interop with the view.

        public void OnClick()
        {
            using (Atom.NoWatch)
            {
                Widget.OnClick?.Invoke();
            }
        }
    }

}