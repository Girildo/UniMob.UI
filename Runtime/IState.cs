using JetBrains.Annotations;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI
{
    public interface IState
    {
        Key Key { get; }

        Widget RawWidget { get; }

        RenderObject RenderObject { get; }

        LayoutConstraints Constraints { get; }

        BuildContext Context { get; }

        IViewState InnerViewState { get; }

        WidgetSize Size { get; }

        Lifetime StateLifetime { get; }

        void UpdateConstraints(LayoutConstraints constraints);
        
        
        /// <summary>
        /// <b>[Atom]</b> Performs re-layout on RenderObject if necessary (e.g. constraints or dependencies have changed)
        /// and subscribes to future re-layouts via the UniMob's reactivity system.
        /// </summary>
        /// <remarks>
        /// This method can be safely called multiple times because
        /// it will not cause the layout to be recalculated every time.
        /// <para>
        /// Every call re-subscribes to <i>any</i> change produced by the layout pass, including changes
        /// that only reposition this widget's own children (e.g. a ScrollList's contents shifting as it
        /// scrolls) without altering this widget's own <see cref="Size"/>. Prefer <see cref="WatchedSize"/>
        /// when only the size is needed -- using this method for that purpose makes the caller re-run on
        /// every purely-internal reposition of the child's subtree, which can cascade up an entire
        /// ancestor chain on something as routine as a nested list scrolling.
        /// </para>
        /// </remarks>
        /// <returns>Final render size of the widget.</returns>
        Vector2 WatchedPerformLayout();

        /// <summary>
        /// <b>[Atom]</b> Performs re-layout on RenderObject if necessary, like <see cref="WatchedPerformLayout"/>,
        /// but only invalidates subscribers when the resulting <see cref="Size"/> actually changes.
        /// </summary>
        /// <remarks>
        /// Use this instead of <see cref="WatchedPerformLayout"/> when measuring a child purely to learn its
        /// size (e.g. from <c>RenderObject.LayoutChild</c>). This decouples a parent's sizing pass from a
        /// child's internal repositioning (scrolling, etc.) that doesn't affect the child's own size.
        /// </remarks>
        /// <returns>Final render size of the widget.</returns>
        Vector2 WatchedSize();

        /// <summary>
        /// Gets diagnostics info useful for debugging and locating the state in the tree.
        /// Returning <c>null</c> means no such info are meaningful.
        /// </summary>
        [CanBeNull]
        public string GetDiagnosticInfo();
    }
}