using JetBrains.Annotations;
using UnityEngine;

namespace UniMob.UI
{
    public interface IView
    {
        // ReSharper disable once InconsistentNaming
        GameObject gameObject { get; }

        // ReSharper disable once InconsistentNaming
        RectTransform rectTransform { get; }

        bool IsDestroyed { get; }

        /// <summary>
        ///     The state this view is currently showing, or null before it has been given one.
        /// </summary>
        /// <remarks>
        ///     The exact inverse of <see cref="IViewState.MountedView"/>, and added as its deliberate
        ///     pair: that one answers "where on screen is this widget", this one answers "which widget
        ///     is this GameObject". Together they are what lets a tool walk up from a scene selection to
        ///     the nearest view and back down again.
        ///     <para>
        ///         Read-only and observational. Setting the source stays <see cref="SetSource"/>'s job.
        ///     </para>
        /// </remarks>
        [CanBeNull]
        IState Source { get; }

        void SetSource(IState source, bool link);
        void ResetSource();
    }
}
