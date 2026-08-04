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

        public override State CreateState() => new TabsState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderTabs((ITabsLayoutState) state);
        }

        // Which page is showing, which the tree cannot say: every tab is laid out, and the ones either
        // side of the current one sit just outside the viewport rather than being absent.
        public override string GetDiagnosticInfo()
        {
            var controller = TabController;
            return controller == null ? null : $"{controller.Index + 1}/{controller.TabCount}";
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

        // The masked variant of the layout view every multi-child layout shares, and not a choice:
        // the pages either side of the current one are always laid out just outside the viewport, so
        // an unmasked Tabs is not a different look, it is one that paints over its surroundings.
        public override WidgetViewReference View =>
            WidgetViewReference.Resource("$$_Layout.MaskedMultiChildLayoutView");
    }
}
