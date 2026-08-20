using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UniMob.UI.Internal;
using UniMob.UI.Layout.Internal;
using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Layout.Internal.Views;
using UnityEngine.UI;
using ScrollListView = UniMob.UI.Layout.Internal.Views.ScrollListView;
using Vector2 = UnityEngine.Vector2;

namespace UniMob.UI.Layout
{
    public class ScrollList : StatefulWidget
    {
        /// <summary>
        ///     Eagerly-built children. Mutually exclusive with <see cref="ItemBuilder"/>/<see cref="ItemCount"/>.
        /// </summary>
        public List<Widget> Children { get; set; } = new();

        /// <summary>
        ///     Builds the widget for the item at <c>index</c> on demand, only for items near the viewport.
        ///     Requires <see cref="ItemCount"/> to be set, and is mutually exclusive with <see cref="Children"/>.
        /// </summary>
        public IndexedWidgetBuilder ItemBuilder { get; set; }

        /// <summary>
        ///     The total number of items when using <see cref="ItemBuilder"/>.
        /// </summary>
        public int? ItemCount { get; set; }

        /// <summary>
        ///     When set, every item is assumed to have exactly this size along the scrolling axis. This lets the
        ///     list compute exact positions/total size without measuring or estimating -- the recommended option
        ///     for uniformly-sized items. Only meaningful together with <see cref="ItemBuilder"/>.
        /// </summary>
        public float? ItemExtent { get; set; }

        /// <summary>
        ///     Resolves a <see cref="Key"/> to its item index for <c>ScrollTo(Key)</c> under <see cref="ItemBuilder"/>,
        ///     where not every item is necessarily built yet. Returns <c>null</c> if the key is unresolvable.
        ///     If not provided, <c>ScrollTo(Key)</c> only resolves against items that have been built at least once.
        /// </summary>
        public Func<Key, int?> KeyToIndexResolver { get; set; }

        public Axis Axis { get; set; } = Axis.Vertical;
        public ScrollController ScrollController { get; set; }

        public float Spacing { get; set; } = 0;

        public bool UseMask { get; set; } = true;

        /// <summary>
        /// Defines how the scroll content behaves when the user scrolls.
        /// <see cref="MovementType"/> for more details."/>
        /// </summary>
        public MovementType MovementType { get; set; } = MovementType.Elastic;

        /// <summary>
        ///     The size, in pixels, of the (bidirectional) cache extent for virtualization.
        ///     <para>If the value is not set, the cache extent will be determined automatically based on the viewport size.</para>
        /// </summary>
        public float? VirtualizationCacheExtent { get; set; }

        public override State CreateState()
        {
            return new ScrollListState();
        }

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderSliverList((ISliverState)state);
        }

        // The logical count, off the widget's own fields. Not the visible window: the state's Children
        // is a computed atom that BUILDS the states it returns, and NoWatch suppresses the dependency,
        // not the work. A list is also the one widget whose emptiness explains a whole blank screen.
        public override string GetDiagnosticInfo()
        {
            var count = ItemCount ?? Children?.Count ?? 0;
            return count == 1 ? "1 item" : count + " items";
        }
    }

    // The reactive half of the virtualized list -- the full architecture & pitfalls are documented at the top of
    // RenderSliverList.cs; this file only adds the reactive-bridge hazards specific to it.
    //
    // It implements two interfaces on purpose: ISliverState/IScrollingListState feed the RenderObject the *full*
    // logical shape (it needs every item, visible or not, to compute layout), while IMultiChildLayoutState exposes
    // only the *visible* States to the View. The imperative layout pass writes indices while the View reads a
    // computed [Atom] -- that impedance mismatch is why this class exists, and why the NoWatch scoping below is fiddly.
    public class ScrollListState
        : ViewState<ScrollList>,
            ISliverState,
            IScrollingListState,
            IScrollControllerExecutor
    {
        private readonly StateCollectionHolder _allChildren;
        private readonly Dictionary<Key, int> _childKeyToIndexMap = new();

        // The shared lazy-build + visible-index reactive bridge (see VirtualizedChildren). The eager path
        // stays here (below) because it goes through the protected CreateChildren; the bridge owns the rest.
        private readonly VirtualizedChildren _virtualized;

        public float Spacing => this.Widget.Spacing;

        private bool IsLazy => Widget.ItemBuilder != null;

        [CanBeNull]
        private ScrollListView _view;

        public ScrollListState()
        {
            _allChildren = CreateChildren(context =>
            {
                var children = Widget.Children;
                _childKeyToIndexMap.Clear();
                for (var i = 0; i < children.Count; i++)
                {
                    var key = children[i]?.Key;
                    if (key != null)
                        _childKeyToIndexMap.Add(key, i);
                }

                return children;
            });

            _virtualized = new VirtualizedChildren(
                StateLifetime,
                new BuildContext(this, Context),
                () => Widget.ItemBuilder,
                ResolveEagerIndex
            );
        }

        // Eager-mode resolver injected into the shared bridge: maps a visible index to its built child State
        // from the CreateChildren collection (null when out of range, matching the prior inline behavior).
        private IState ResolveEagerIndex(int index)
        {
            var all = _allChildren.Value;
            return index < all.Length ? all[index] : null;
        }

        [Atom]
        IState[] IMultiChildLayoutState.Children => _virtualized.VisibleChildren;

        [Atom]
        public bool UseMask => Widget.UseMask;

        [Atom]
        public MovementType MovementType => Widget.MovementType;

        [Atom]
        public ScrollController ScrollController { get; private set; }

        [Atom]
        public Vector2 ViewportSize { get; set; }

        // Pixel offset, not NormalizedValue -- see ScrollController.PixelOffset's doc for why RenderSliverList's
        // estimation-based lazy windowing needs an absolute value that doesn't drift when its own estimated
        // total content size changes between layout passes.
        [Atom]
        public float ScrollPixelOffset => ScrollController.PixelOffset;

        [Atom]
        public IState[] AllChildren => IsLazy ? Array.Empty<IState>() : _allChildren.Value;

        [Atom]
        public int? ItemCount => Widget.ItemCount;

        [Atom]
        public float? ItemExtent => Widget.ItemExtent;

        [Atom]
        public Axis Axis => Widget.Axis;

        [Atom]
        public float? VirtualizationCacheExtent => Widget.VirtualizationCacheExtent;

        public override void DidViewMount(IView view)
        {
            base.DidViewMount(view);
            _view = view as ScrollListView;
        }

        public override void DidViewUnmount(IView view)
        {
            base.DidViewUnmount(view);
            _view = null;
        }

        public override WidgetViewReference View =>
            WidgetViewReference.Resource("Layout/UniMob.ScrollList");

        float? ISliverState.VirtualizationCacheExtent => VirtualizationCacheExtent;

        void ISliverState.SetVisibleChildren(List<IndexedLayoutData> visibleChildren) =>
            _virtualized.SetVisibleChildren(visibleChildren);

        IState[] ISliverState.RequestBuildWindow(int startIndexInclusive, int endIndexExclusive) =>
            _virtualized.RequestBuildWindow(startIndexInclusive, endIndexExclusive);

        public override void InitState()
        {
            base.InitState();

            ValidateMode();

            // Use the provided controller or create a new one.
            ScrollController = Widget.ScrollController ?? new ScrollController(StateLifetime);
            ScrollController.Attach(this);

            // ScrollController reads this lazily on dispose, so it always detaches from whichever
            // controller is current at that point, even if DidUpdateWidget swapped it in the meantime.
            StateLifetime.Register(() => ScrollController.Detach(this));
        }

        public override void DidUpdateWidget(ScrollList oldWidget)
        {
            base.DidUpdateWidget(oldWidget);

            ValidateMode();

            if (Widget.ScrollController != null && Widget.ScrollController != ScrollController)
            {
                ScrollController.Detach(this);
                ScrollController = Widget.ScrollController;
                ScrollController.Attach(this);
            }
        }

        private void ValidateMode()
        {
            var hasBuilder = Widget.ItemBuilder != null;
            var hasChildren = Widget.Children is { Count: > 0 };

            if (hasBuilder && hasChildren)
                throw new InvalidOperationException(
                    "ScrollList cannot have both ItemBuilder and Children set -- use ItemBuilder+ItemCount "
                        + "for lazy building, or Children for eager building, not both."
                );

            if (hasBuilder && Widget.ItemCount == null)
                throw new InvalidOperationException(
                    "ScrollList.ItemCount must be set when ItemBuilder is provided."
                );

            if (!hasBuilder && Widget.ItemCount != null)
                throw new InvalidOperationException(
                    "ScrollList.ItemCount has no effect without ItemBuilder."
                );
        }

        bool IScrollControllerExecutor.ScrollTo(
            int index,
            float duration,
            ScrollToPosition position,
            Easing easing
        )
        {
            return _view?.ScrollTo(index, duration, position, easing) ?? false;
        }

        bool IScrollControllerExecutor.ScrollTo(
            Key key,
            float duration,
            ScrollToPosition position,
            Easing easing
        )
        {
            int index;

            if (IsLazy)
            {
                if (Widget.KeyToIndexResolver != null)
                {
                    var resolved = Widget.KeyToIndexResolver(key);
                    if (resolved == null)
                        return false;
                    index = resolved.Value;
                }
                else if (!_virtualized.TryResolveSeenKey(key, out index))
                {
                    return false;
                }
            }
            else if (!_childKeyToIndexMap.TryGetValue(key, out index))
            {
                return false;
            }

            return ((IScrollControllerExecutor)this).ScrollTo(index, duration, position, easing);
        }
    }
}
