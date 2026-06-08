using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI.Layout
{
    public delegate T ConstrainedBuilderDelegate<out T>(BuildContext context, LayoutConstraints constraints) where T : Widget;
    
    public class ConstrainedBuilder : SingleChildLayoutWidget
    {
        public ConstrainedBuilderDelegate<Widget> Builder { get; set; }

        public override State CreateState() => new ConstrainedBuilderState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state) => new RenderProxy((ConstrainedBuilderState)state);
    }

    public class ConstrainedBuilderState : ViewState<ConstrainedBuilder>, ISingleChildLayoutState
    {
        private readonly StateHolder _child;

        public ConstrainedBuilderState()
        {
            _child = CreateChild(context => Widget.Builder?.Invoke(context, Constraints));
        }

        // Fulfill the ISingleChildLayoutState interface
        public IState Child => _child?.Value;

        // Provide the non-painting layout view reference standard for single child layouts
        public override WidgetViewReference View =>
            WidgetViewReference.Resource("$$_Layout.SingleChildLayoutView");
    }
}
