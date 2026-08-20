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

        Lifetime StateLifetime { get; }

        // Layout is reached through RenderObject, not through here. A state is an element: it owns a
        // widget, builds children and reconciles. Answering for a render object's constraints or size
        // as well would let several states answer for one render object, and would invite the question
        // "how big am I" before anything has laid this out -- which has no answer.

        /// <summary>
        /// Gets diagnostics info useful for debugging and locating the state in the tree.
        /// Returning <c>null</c> means no such info are meaningful.
        /// </summary>
        [CanBeNull]
        public string GetDiagnosticInfo();
    }
}