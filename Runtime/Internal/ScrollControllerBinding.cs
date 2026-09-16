using System;
using UniMob.UI.Internal.Views;
using UniMob.UI.Rendering;

namespace UniMob.UI.Internal
{
    /// <summary>
    ///     The link between a virtualized scrollable's state and the <see cref="ScrollController" />
    ///     driving it: it decides which controller is current, keeps the attachment in step with the
    ///     widget, and answers the controller's requests on the state's behalf.
    /// </summary>
    /// <remarks>
    ///     An object rather than a set of members on the state, so that one implementation serves both
    ///     <c>ScrollList</c> and <c>ScrollGrid</c>. It is an <see cref="ILifetimeScope" /> because it
    ///     carries <c>[Atom]</c> members of its own, on the owning state's lifetime.
    /// </remarks>
    internal sealed class ScrollControllerBinding : IScrollControllerExecutor, ILifetimeScope
    {
        private readonly IScrollingListState _owner;
        private readonly VirtualizedChildren _children;
        private readonly Func<Func<Key, int?>?> _keyToIndexResolver;

        private ScrollListView? _view;
        private bool _detachRegistered;

        public ScrollControllerBinding(
            IScrollingListState owner,
            VirtualizedChildren children,
            Func<Func<Key, int?>?> keyToIndexResolver
        )
        {
            _owner = owner;
            _children = children;
            _keyToIndexResolver = keyToIndexResolver;
        }

        public Lifetime Lifetime => _owner.StateLifetime;

        public IState Owner => _owner;

        /// <summary>
        ///     <b>[Atom]</b> The controller currently driving the scrollable: the one its widget brought,
        ///     or one of the binding's own. Null until the first <see cref="Bind" />, which the owner runs
        ///     from <c>InitState</c>.
        /// </summary>
        [Atom]
        public ScrollController Controller { get; private set; } = null!;

        /// <summary>
        ///     Makes <paramref name="widgetController" /> the current controller, detaching whichever one
        ///     was current before. Called from <c>InitState</c> and from <c>DidUpdateWidget</c>. A widget
        ///     that stops naming a controller keeps the one it named: dropping back to a controller of our
        ///     own would silently lose the position the caller still holds a controller for.
        /// </summary>
        public void Bind(ScrollController? widgetController)
        {
            ScrollController? current;

            // Untracked: Bind runs from lifecycle callbacks, which must not subscribe whatever
            // computation drove them to the controller they happen to swap.
            using (Atom.NoWatch)
            {
                current = Controller;
            }

            var next = widgetController ?? current ?? new ScrollController(Lifetime);
            if (ReferenceEquals(current, next))
                return;

            current?.Detach(this);
            Controller = next;
            next.Attach(this);

            if (_detachRegistered)
                return;

            _detachRegistered = true;

            // Read lazily on dispose, so it detaches from whichever controller is current at that
            // point rather than from the one bound here.
            Lifetime.Register(() => Controller.Detach(this));
        }

        /// <summary>
        ///     Points the binding at the mounted view that performs the scrolling, or at nothing once it
        ///     unmounts. Called from <c>DidViewMount</c> / <c>DidViewUnmount</c>.
        /// </summary>
        public void AttachView(ScrollListView? view) => _view = view;

        /// <summary>
        ///     <b>[Atom]</b> The scrollable's geometry along its scroll axis, or <c>null</c> before
        ///     anything has laid it out.
        /// </summary>
        [Atom]
        public ScrollMetrics? Metrics
        {
            get
            {
                var renderObject = _owner.RenderObject;

                // Constraints first, and never WatchLayout on a render object nothing has laid out:
                // the constraints atom is what a reader has to depend on to wake on the first pass,
                // and WatchLayout reports a never-laid-out read as an error in the Editor.
                if (renderObject == null || !renderObject.Constraints.HasValue)
                    return null;

                // WatchLayout, not WatchedSize: TotalContentSize() is a plain method whose lazy
                // estimate refines on passes that leave the viewport exactly as big as it was.
                var viewport = renderObject.WatchLayout();
                var axis = _owner.Axis;

                return new ScrollMetrics(
                    Controller.PixelOffset,
                    ((IScrollableRenderObject)renderObject).TotalContentSize(),
                    axis == Axis.Horizontal ? viewport.x : viewport.y,
                    axis
                );
            }
        }

        public bool ScrollTo(
            int index,
            float duration,
            ScrollToPosition position,
            Easing? easing
        ) => _view?.ScrollTo(index, duration, position, easing) ?? false;

        public bool ScrollTo(Key key, float duration, ScrollToPosition position, Easing? easing) =>
            _children.TryResolveIndex(key, _keyToIndexResolver(), out var index)
            && ScrollTo(index, duration, position, easing);

        public void SnapToControllerOffset()
        {
            // The controller owns the offset and has already written it; a scrollable with no mounted
            // view has no animation or inertia to stop.
            _view?.SnapToControllerOffset();
        }
    }
}
