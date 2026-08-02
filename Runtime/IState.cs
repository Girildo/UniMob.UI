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

        /// <summary>
        /// The render object this state owns. Every state owns exactly one, so this is total: layout
        /// is reached through here, and the reactive layout members live on it rather than here.
        /// </summary>
        RenderObject RenderObject { get; }

        BuildContext Context { get; }

        IViewState InnerViewState { get; }

        WidgetSize Size { get; }

        Lifetime StateLifetime { get; }

        // Layout used to be reached through here: UpdateConstraints to push, WatchedPerformLayout and
        // WatchedSize to observe, Constraints to read back. All four moved to RenderObject, which is
        // the thing being laid out. Asking a state to answer them meant several states could answer
        // for one render object, and meant a state could be asked for constraints before anything had
        // laid it out -- a question with no answer, which the old accessor papered over.

        /// <summary>
        /// Gets diagnostics info useful for debugging and locating the state in the tree.
        /// Returning <c>null</c> means no such info are meaningful.
        /// </summary>
        [CanBeNull]
        public string GetDiagnosticInfo();
    }
}