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
        /// </remarks>
        /// <returns>Final render size of the widget.</returns>
        Vector2 WatchedPerformLayout();

        /// <summary>
        /// Gets diagnostics info useful for debugging and locating the state in the tree.
        /// Returning <c>null</c> means no such info are meaningful.
        /// </summary>
        [CanBeNull]
        public string GetDiagnosticInfo();
    }
}