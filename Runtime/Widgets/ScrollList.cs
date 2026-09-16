using System;
using System.Collections.Generic;
using UniMob.UI.Internal;
using UniMob.UI.Rendering;
using ScrollListView = UniMob.UI.Internal.Views.ScrollListView;
using Vector2 = UnityEngine.Vector2;

namespace UniMob.UI.Widgets
{
    public class ScrollList : StatefulWidget
    {
        /// <summary>
        ///     Eagerly-built children. Mutually exclusive with <see cref="ItemBuilder"/>/<see cref="ItemCount"/>.
        /// </summary>
        public List<Widget> Children { get; init; } = new();

        /// <summary>
        ///     Builds the widget for the item at <c>index</c> on demand, only for items near the viewport.
        ///     Requires <see cref="ItemCount"/> to be set, and is mutually exclusive with <see cref="Children"/>.
        /// </summary>
        public IndexedWidgetBuilder? ItemBuilder { get; init; }

        /// <summary>
        ///     The total number of items when using <see cref="ItemBuilder"/>.
        /// </summary>
        public int? ItemCount { get; init; }

        /// <summary>
        ///     When set, every item is assumed to have exactly this size along the scrolling axis. This lets the
        ///     list compute exact positions/total size without measuring or estimating -- the recommended option
        ///     for uniformly-sized items. Only meaningful together with <see cref="ItemBuilder"/>.
        /// </summary>
        public float? ItemExtent { get; init; }

        /// <summary>
        ///     Resolves a <see cref="Key"/> to its item index for <c>ScrollTo(Key)</c> under <see cref="ItemBuilder"/>,
        ///     where not every item is necessarily built yet. Returns <c>null</c> if the key is unresolvable.
        ///     If not provided, <c>ScrollTo(Key)</c> only resolves against items that have been built at least once.
        /// </summary>
        public Func<Key, int?>? KeyToIndexResolver { get; init; }

        public Axis Axis { get; init; } = Axis.Vertical;
        public ScrollController? ScrollController { get; init; }

        public float Spacing { get; init; } = 0;

        public bool UseMask { get; init; } = true;

        /// <summary>
        /// Defines how the scroll content behaves when the user scrolls.
        /// <see cref="MovementType"/> for more details."/>
        /// </summary>
        public MovementType MovementType { get; init; } = MovementType.Elastic;

        /// <summary>
        ///     The size, in pixels, of the (bidirectional) cache extent for virtualization.
        ///     <para>If the value is not set, the cache extent will be determined automatically based on the viewport size.</para>
        /// </summary>
        public float? VirtualizationCacheExtent { get; init; }

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
    // computed [Atom] -- that impedance mismatch is why this class exists, and VirtualizedChildren straddles it.
    public class ScrollListState : ViewState<ScrollList>, ISliverState, IScrollingListState
    {
        private readonly StateCollectionHolder _allChildren;

        // The shared child-addressing bridge: lazy building, visible indices and key-to-index resolution
        // (see VirtualizedChildren). The eager States are built here, because that goes through the
        // protected CreateChildren.
        private readonly VirtualizedChildren _virtualized;

        private readonly ScrollControllerBinding _binding;

        public float Spacing => this.Widget.Spacing;

        private bool IsLazy => Widget.ItemBuilder != null;

        public ScrollListState()
        {
            _allChildren = CreateChildren(IndexEagerChildren);

            _virtualized = new VirtualizedChildren(
                StateLifetime,
                new BuildContext(this, Context),
                () => Widget.ItemBuilder,
                _allChildren
            );

            _binding = new ScrollControllerBinding(
                this,
                _virtualized,
                () => Widget.KeyToIndexResolver
            );
        }

        // The builder runs on the first pull of _allChildren, which is long after the constructor
        // above has assigned _virtualized.
        private List<Widget> IndexEagerChildren(BuildContext context) =>
            _virtualized.IndexEagerKeys(Widget.Children, this);

        [Atom]
        IState[] IMultiChildLayoutState.Children => _virtualized.VisibleChildren;

        [Atom]
        public bool UseMask => Widget.UseMask;

        [Atom]
        public MovementType MovementType => Widget.MovementType;

        [Atom]
        public ScrollController ScrollController => _binding.Controller;

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
            _binding.AttachView(view as ScrollListView);
        }

        public override void DidViewUnmount(IView view)
        {
            base.DidViewUnmount(view);
            _binding.AttachView(null);
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

            _binding.Bind(Widget.ScrollController);
        }

        public override void DidUpdateWidget(ScrollList oldWidget)
        {
            base.DidUpdateWidget(oldWidget);

            ValidateMode();

            _binding.Bind(Widget.ScrollController);
        }

        private void ValidateMode() =>
            ScrollableWidgetValidation.ValidateChildrenMode(
                nameof(ScrollList),
                Widget.ItemBuilder != null,
                Widget.Children is { Count: > 0 },
                Widget.ItemCount
            );
    }
}
