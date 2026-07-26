using System.Collections.Generic;

namespace UniMob.UI.Layout
{
    /// <summary>
    /// Horizontally paged tabs driven by a <see cref="TabController"/>, with an optional edge mask and
    /// drag-to-switch. Modern-layout facade over the legacy <c>UniMob.UI.Widgets.Tabs</c>, whose interactive
    /// view (mask, drag, paging) lives in a Unity prefab; this exposes it to modern trees via the legacy
    /// interop bridge, mirroring how the modern <see cref="AnimatedSwitcher"/> wraps its legacy counterpart.
    /// Coexists with the legacy widget; told apart by namespace. For a fixed size, wrap it in a modern
    /// <see cref="SizedBox"/>/<see cref="ConstrainedBox"/> rather than exposing a legacy <c>WidgetSize</c>.
    /// </summary>
    public class Tabs : StatefulWidget
    {
        public Tabs(TabController tabController)
        {
            TabController = tabController;
        }

        public TabController TabController { get; }
        public List<Widget> Children { get; set; } = new();
        public bool UseMask { get; set; } = true;
        public bool Draggable { get; set; } = true;
        public AxisSize CrossAxisSize { get; set; } = AxisSize.Min;
        public AxisSize MainAxisSize { get; set; } = AxisSize.Min;

        public override State CreateState() => new TabsState();
    }

    internal class TabsState : HocState<Tabs>
    {
        public override Widget Build(BuildContext context)
        {
            return new UniMob.UI.Widgets.Tabs(Widget.TabController)
            {
                Children = Widget.Children,
                UseMask = Widget.UseMask,
                Draggable = Widget.Draggable,
                CrossAxisSize = Widget.CrossAxisSize,
                MainAxisSize = Widget.MainAxisSize,
            };
        }
    }
}
