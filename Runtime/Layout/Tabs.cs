using System.Collections.Generic;
using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI.Layout
{
    /// <summary>
    ///     Horizontally paged tabs driven by a <see cref="TabController"/>: every child is one page the size
    ///     of the viewport, and the run of them slides as the controller moves.
    /// </summary>
    /// <remarks>
    ///     A real modern layout, not a wrapper around the legacy widget: children are laid out by
    ///     <see cref="RenderTabs"/> against real constraints, so a modern subtree is a valid page. The legacy
    ///     <c>UniMob.UI.Widgets.Tabs</c> sized itself by intrinsic-walking its children, which made a modern
    ///     child of it a layout error.
    ///     <para>
    ///     Swipe-to-switch is deliberately not implemented. The legacy widget carried it in its view, along
    ///     with the rule that routes a mostly-vertical drag to the parent so a scrollable page still scrolls.
    ///     No caller uses it. If one needs it, wrap this in a <see cref="GestureDetector"/> and drive the
    ///     controller from <c>OnDragUpdate</c>, rather than rebuilding gesture handling in the view.
    ///     </para>
    /// </remarks>
    public class Tabs : StatefulWidget, IMultiChildLayoutWidget
    {
        public Tabs(TabController tabController)
        {
            TabController = tabController;
        }

        public TabController TabController { get; }

        /// <summary>One page each, in tab order.</summary>
        public List<Widget> Children { get; set; } = new();

        /// <summary>
        ///     Clips the pages to the viewport. On by default, because the pages either side of the current
        ///     one are laid out just outside it and would otherwise paint over whatever surrounds the tabs.
        /// </summary>
        public bool UseMask { get; set; } = true;

        public override State CreateState() => new TabsState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderTabs((ITabsLayoutState) state);
        }
    }

    internal class TabsState : ViewState<Tabs>, ITabsLayoutState
    {
        private readonly StateCollectionHolder _children;

        public TabsState()
        {
            _children = CreateChildren(context => Widget.Children);
        }

        public IState[] Children => _children.Value;

        public TabController TabController => Widget.TabController;

        // The mask is a component on the view, so switching it means switching views. Reactive, so
        // UseMask can change at runtime.
        [Atom]
        public override WidgetViewReference View => Widget.UseMask
            ? WidgetViewReference.Resource("$$_Layout.MaskedMultiChildLayoutView")
            : WidgetViewReference.Resource("$$_Layout.MultiChildLayoutView");
    }
}
