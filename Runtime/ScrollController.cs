using System;
using UniMob.UI;
using UniMob.UI.Diagnostics;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI
{
    public class ScrollController : ILifetimeScope
    {
        // The binding is an atom so that "nothing attached yet" is observable: a reader of Metrics or
        // IsAttached created before a list mounts depends on the binding itself and wakes when it is
        // written, rather than sleeping forever on an empty dependency list.
        private readonly MutableAtom<IScrollControllerExecutor?> _executor = Atom.Value(
            default(IScrollControllerExecutor?)
        );

        public ScrollController(Lifetime lifetime)
        {
            Lifetime = lifetime;
        }

        public Lifetime Lifetime { get; }

        // Untracked on purpose: the imperative members must not add a dependency to whatever
        // computation their caller happens to be inside. Metrics and IsAttached read the atom
        // directly, and are the reactive half of the same binding.
        private IScrollControllerExecutor? Executor
        {
            get
            {
                using (Atom.NoWatch)
                {
                    return _executor.Value;
                }
            }
            set => _executor.Value = value;
        }

        /// <summary>
        ///     <b>[Atom]</b> The attached scrollable's geometry along its scroll axis, or <c>null</c>
        ///     while nothing is attached or the attached scrollable has not been laid out yet.
        ///     Observers created before a scrollable attaches wake when it does.
        /// </summary>
        [Atom]
        public ScrollMetrics? Metrics => _executor.Value?.Metrics;

        /// <summary>
        ///     <b>[Atom]</b> Whether a scrollable is currently attached to this controller.
        /// </summary>
        public bool IsAttached => _executor.Value != null;

        /// <summary>
        ///     <b>[Atom]</b> <see cref="PixelOffset"/> as a 0..1 ratio of the scrollable range, clamped
        ///     at both ends so an Elastic overscroll reads as exactly 0 or 1. Zero while nothing is
        ///     attached, while the attached scrollable has not been laid out, and whenever the content
        ///     fits its viewport.
        /// </summary>
        [Atom]
        public float NormalizedValue
        {
            get
            {
                if (Metrics is { CanScroll: true } metrics)
                    return Mathf.Clamp01(metrics.PixelOffset / metrics.MaxScrollExtent);

                return 0f;
            }
        }

        /// <summary>
        ///     The current scroll offset in pixels along the scrolling axis, measured from the start
        ///     (top/left) of the content. Absolute, so it does not drift when the total content size
        ///     changes underneath it -- which a lazy list's estimator does on essentially every layout
        ///     pass. <see cref="NormalizedValue"/> expresses the same position as a ratio of the
        ///     scrollable range, and therefore does move with that estimate.
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
            Easing? easing = null
        )
        {
            return Executor?.ScrollTo(
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
            Easing? easing = null
        )
        {
            return Executor?.ScrollTo(
                    key,
                    duration,
                    position ?? ScrollToPosition.Start,
                    easing ?? Ease.InOutCirc
                ) ?? false;
        }

        /// <summary>
        ///     Moves the attached scrollable to <paramref name="pixelOffset"/> immediately, stopping any
        ///     running <see cref="ScrollTo(int, float, ScrollToPosition?, Easing)"/> animation and any
        ///     ScrollRect inertia. The offset is clamped to <c>[0, MaxScrollExtent]</c>; before the
        ///     scrollable has been laid out there is no known upper bound, so only the lower one applies.
        /// </summary>
        /// <returns><c>false</c> if no scrollable is currently attached to this controller.</returns>
        public bool JumpTo(float pixelOffset)
        {
            var executor = Executor;
            if (executor == null)
                return false;

            ScrollMetrics? metrics;

            // Untracked: reading the metrics reaches the attached scrollable's layout, and a jump
            // issued from inside a computation must not make that computation depend on it.
            using (Atom.NoWatch)
            {
                metrics = executor.Metrics;
            }

            var target = metrics.HasValue
                ? Mathf.Clamp(pixelOffset, 0f, metrics.Value.MaxScrollExtent)
                : Mathf.Max(0f, pixelOffset);

            PixelOffset = target;
            executor.SnapToControllerOffset();
            return true;
        }

        /// <summary>
        ///     Binds the currently-mounted scrollable as the executor of this controller's scroll
        ///     requests. Only one may be attached at a time: attaching replaces any previous executor,
        ///     which then stops responding to this controller, and reports that as a fault.
        /// </summary>
        internal void Attach(IScrollControllerExecutor executor)
        {
            var replaced = Executor;

            Executor = executor;

            if (replaced == null || ReferenceEquals(replaced, executor))
                return;

            // Deferred by a frame because a keyed reorder inflates the replacement list before
            // deactivating the list it replaces, and detach runs from the old state's lifetime: at
            // this instant a legitimate reorder is indistinguishable from two live scrollables.
            Zone.Current?.NextFrame(() => ReportDoubleAttach(replaced, executor));
        }

        /// <summary>
        ///     Unbinds <paramref name="executor"/> from this controller, if it is the currently attached one.
        /// </summary>
        internal void Detach(IScrollControllerExecutor executor)
        {
            if (ReferenceEquals(Executor, executor))
                Executor = null;
        }

        private static void ReportDoubleAttach(
            IScrollControllerExecutor replaced,
            IScrollControllerExecutor current
        )
        {
            if (replaced is IState replacedState && replacedState.StateLifetime.IsDisposed)
                return;

            UniMobError.Report(
                new UniMobFault(
                    new InvalidOperationException(
                        $"One ScrollController drives two scrollables at once: {Describe(replaced)} "
                            + $"and {Describe(current)}. The most recent attachment wins, and the other "
                            + "scrollable no longer responds to this controller. Give each scrollable a "
                            + "ScrollController of its own."
                    ),
                    "ScrollController.Attach",
                    current as IState
                )
            );
        }

        private static string Describe(IScrollControllerExecutor executor) =>
            executor is State state ? state.ToDiagnosticString() : executor.GetType().Name;
    }

    /// <summary>
    ///     Implemented by the scrolling list state that a <see cref="ScrollController"/> is attached to, so the
    ///     controller can forward <see cref="ScrollController.ScrollTo(int, float, ScrollToPosition?, Easing)"/>
    ///     requests to it without needing a direct reference (e.g. via <see cref="GlobalKey{T}"/>).
    /// </summary>
    internal interface IScrollControllerExecutor
    {
        /// <summary>
        ///     <b>[Atom]</b> This scrollable's geometry along its scroll axis, or <c>null</c> before
        ///     anything has laid it out.
        /// </summary>
        ScrollMetrics? Metrics { get; }

        bool ScrollTo(int index, float duration, ScrollToPosition position, Easing? easing);
        bool ScrollTo(Key key, float duration, ScrollToPosition position, Easing? easing);

        /// <summary>
        ///     Stops any running scroll animation and any inertia, and lands on
        ///     <see cref="ScrollController.PixelOffset"/>, which the controller has already written.
        /// </summary>
        void SnapToControllerOffset();
    }
}
