using UniMob.UI.Layout;

namespace UniMob.UI
{
    public class ScrollController : ILifetimeScope
    {
        private IScrollControllerExecutor _executor;

        public ScrollController(Lifetime lifetime)
        {
            Lifetime = lifetime;
        }

        public Lifetime Lifetime { get; }

        [Atom]
        public float NormalizedValue { get; internal set; }

        /// <summary>
        ///     The current scroll offset in pixels along the scrolling axis, measured from the start (top/left)
        ///     of the content. Unlike <see cref="NormalizedValue"/> (a 0..1 ratio against the total content
        ///     size), this is an absolute value that doesn't drift when the total content size estimate changes
        ///     underneath it -- which <see cref="Layout.ScrollList"/>'s lazy-building estimator does on
        ///     essentially every layout pass. Used internally by <see cref="Layout.ScrollList"/>; other
        ///     scrollable widgets that share this controller type are unaffected (they never read/write it).
        /// </summary>
        [Atom]
        public float PixelOffset { get; internal set; }

        /// <summary>
        ///     Scrolls to the item at the given index, if this controller is currently attached to a
        ///     mounted <see cref="ScrollList"/>.
        /// </summary>
        /// <returns><c>false</c> if no list is currently attached to this controller.</returns>
        public bool ScrollTo(
            int index,
            float duration = 0,
            ScrollToPosition? position = null,
            Easing easing = null
        )
        {
            return _executor?.ScrollTo(
                    index,
                    duration,
                    position ?? ScrollToPosition.Start,
                    easing ?? Ease.InOutCirc
                ) ?? false;
        }

        /// <summary>
        ///     Scrolls to the item with the given <see cref="Key"/>, if this controller is currently attached to a
        ///     mounted <see cref="ScrollList"/>.
        /// </summary>
        /// <returns><c>false</c> if no list is currently attached to this controller, or no item with the given key exists.</returns>
        public bool ScrollTo(
            Key key,
            float duration = 0,
            ScrollToPosition? position = null,
            Easing easing = null
        )
        {
            return _executor?.ScrollTo(
                    key,
                    duration,
                    position ?? ScrollToPosition.Start,
                    easing ?? Ease.InOutCirc
                ) ?? false;
        }

        /// <summary>
        ///     Binds the currently-mounted <see cref="ScrollList"/> as the executor of this controller's scroll
        ///     requests. Only one list may be attached at a time; attaching replaces any previous executor.
        /// </summary>
        internal void Attach(IScrollControllerExecutor executor)
        {
            _executor = executor;
        }

        /// <summary>
        ///     Unbinds <paramref name="executor"/> from this controller, if it is the currently attached one.
        /// </summary>
        internal void Detach(IScrollControllerExecutor executor)
        {
            if (_executor == executor)
                _executor = null;
        }
    }

    /// <summary>
    ///     Implemented by the scrolling list state that a <see cref="ScrollController"/> is attached to, so the
    ///     controller can forward <see cref="ScrollController.ScrollTo(int, float, ScrollToPosition?, Easing)"/>
    ///     requests to it without needing a direct reference (e.g. via <see cref="GlobalKey{T}"/>).
    /// </summary>
    internal interface IScrollControllerExecutor
    {
        bool ScrollTo(int index, float duration, ScrollToPosition position, Easing easing);
        bool ScrollTo(Key key, float duration, ScrollToPosition position, Easing easing);
    }
}
