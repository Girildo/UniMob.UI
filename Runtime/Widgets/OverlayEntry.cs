using System;

namespace UniMob.UI.Widgets
{
    /// <summary>
    ///     One entry on an <see cref="Overlay"/>: a build-independent identity, owned by whoever
    ///     inserted it and removable from outside the build that created it.
    /// </summary>
    /// <remarks>
    ///     This is what a route was being borrowed for. An entry has no name, no scope, no modality
    ///     and no pop protocol -- only a position on the layer and a way off it -- and unlike a pop, a
    ///     removal cannot silently fail: an entry either leaves the layer or was already gone.
    ///     <para>
    ///         An entry dies with the state that inserted it. That rule is what makes it safe for the
    ///         builder to close over the inserter's context, and it is enforced here rather than left
    ///         to each caller.
    ///     </para>
    /// </remarks>
    public sealed class OverlayEntry
    {
        private readonly OverlayState _overlay;
        private readonly Action? _onDismissed;

        private bool _removed;

        internal OverlayEntry(OverlayState overlay, Action? onDismissed)
        {
            _overlay = overlay;
            _onDismissed = onDismissed;
        }

        /// <summary>Whether this entry is still on the layer. Untracked.</summary>
        public bool IsMounted => !_removed;

        /// <summary>
        ///     Takes this entry off the layer on its owner's behalf. Idempotent, and silent: the caller
        ///     is the one who would be told.
        /// </summary>
        public void Remove() => _overlay.Withdraw(this, dismissed: false);

        /// <summary>
        ///     Takes this entry off the layer on anyone else's behalf -- a tap on its own barrier, most
        ///     often -- and tells its owner so. Idempotent, and fires the callback at most once.
        /// </summary>
        public void Dismiss() => _overlay.Withdraw(this, dismissed: true);

        internal void MarkRemoved() => _removed = true;

        internal void NotifyDismissed() => _onDismissed?.Invoke();
    }
}
