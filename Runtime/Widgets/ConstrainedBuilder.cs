using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniMob.UI.Rendering;

namespace UniMob.UI.Widgets
{
    /// <summary>Builds the widget for one slot from the constraints that slot was given.</summary>
    public delegate TWidget ConstrainedWidgetBuilder<out TWidget>(
        BuildContext context,
        LayoutConstraints constraints
    )
        where TWidget : Widget;

    public class ConstrainedBuilder : SingleChildLayoutWidget
    {
        /// <summary>
        /// Builds the child from the constraints this widget is given. Without one there is no
        /// child, which is the same thing as building nothing.
        /// </summary>
        public ConstrainedWidgetBuilder<Widget>? Builder { get; init; }

        public override State CreateState() => new ConstrainedBuilderState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state) =>
            new RenderProxy((ConstrainedBuilderState)state);
    }

    public class ConstrainedBuilderState : ViewState<ConstrainedBuilder>, ISingleChildLayoutState
    {
        private readonly StateHolder _child;

        public ConstrainedBuilderState()
        {
            // Reading a layout result during a build inverts the usual order, and it resolves only
            // because of a sequence: RenderObject.Layout writes its constraints atom before pulling
            // anything, and the eagerly-created RenderProxy's sizing pass pulls Child afterwards. So
            // the write always lands before this builder runs.
            //
            // The write is what makes this reactive rather than merely lucky -- this build subscribes
            // to the constraints atom, so a later push rebuilds the subtree. That is also why the
            // ordering is load-bearing rather than cosmetic: reverse it and the builder quietly sees
            // the previous frame's constraints. ConstrainedBuilderTests pins it.
            _child = CreateChild(context =>
                Widget.Builder?.Invoke(context, RenderObject.Constraints ?? default)
            );
        }

        // Fulfill the ISingleChildLayoutState interface
        public IState? Child => _child.Value;

        // Provide the non-painting layout view reference standard for single child layouts
        public override WidgetViewReference View =>
            WidgetViewReference.Registered("UniMob.SingleChildLayoutView");
    }
}
