using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UniMob.UI.Layout.Internal;
using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Layout.Internal.Views;

namespace UniMob.UI.Layout
{
    // A virtualized, GridView-style scroll grid: fixed columns (or a column count derived from a max cell
    // width), row-based virtualization, and optional lazy building -- the 2D sibling of ScrollList. It reuses
    // ScrollList's View (ScrollListView / the "Layout/UniMob.ScrollList" prefab) via the shared
    // IScrollableRenderObject seam, and RenderSliverGrid does the geometry. See RenderSliverGrid.cs for the
    // layout model and SliverGridDelegate.cs for the column math.
    public class ScrollGrid : StatefulWidget
    {
        /// <summary>
        ///     Eagerly-built children. Mutually exclusive with <see cref="ItemBuilder" />/<see cref="ItemCount" />.
        /// </summary>
        public List<Widget> Children { get; set; } = new();

        /// <summary>
        ///     Builds the widget for the item at <c>index</c> on demand, only for items near the viewport.
        ///     Requires <see cref="ItemCount" />, and is mutually exclusive with <see cref="Children" />.
        /// </summary>
        public IndexedWidgetBuilder ItemBuilder { get; set; }

        /// <summary>The total number of items when using <see cref="ItemBuilder" />.</summary>
        public int? ItemCount { get; set; }

        /// <summary>
        ///     Resolves a <see cref="Key" /> to its item index for <c>ScrollTo(Key)</c> under
        ///     <see cref="ItemBuilder" />. If not provided, <c>ScrollTo(Key)</c> only resolves keys of items
        ///     that have been built at least once.
        /// </summary>
        public Func<Key, int?> KeyToIndexResolver { get; set; }

        /// <summary>Fixed number of columns (cross axis). Mutually exclusive with <see cref="MaxCrossAxisExtent" />.</summary>
        public int? CrossAxisCount { get; set; }

        /// <summary>
        ///     Fits as many columns as possible so each cell is at most this wide (cross axis). Mutually
        ///     exclusive with <see cref="CrossAxisCount" />.
        /// </summary>
        public float? MaxCrossAxisExtent { get; set; }

        /// <summary>
        ///     Ratio of a cell's cross-axis extent to its main-axis extent. Sets a fixed cell size (exact
        ///     positions, no measuring). Mutually exclusive with <see cref="MainAxisExtent" />. If neither is
        ///     set, rows are measured (each row as tall as its tallest child).
        /// </summary>
        public float? ChildAspectRatio { get; set; }

        /// <summary>
        ///     Fixed cell extent along the scroll axis. Mutually exclusive with <see cref="ChildAspectRatio" />.
        ///     If neither is set, rows are measured.
        /// </summary>
        public float? MainAxisExtent { get; set; }

        /// <summary>Gap between rows along the scroll axis.</summary>
        public float MainAxisSpacing { get; set; } = 0;

        /// <summary>Gap between columns along the cross axis.</summary>
        public float CrossAxisSpacing { get; set; } = 0;

        /// <summary>Padding around the grid content.</summary>
        public RectPadding Padding { get; set; }

        public Axis Axis { get; set; } = Axis.Vertical;
        public ScrollController ScrollController { get; set; }
        public bool UseMask { get; set; } = true;
        public MovementType MovementType { get; set; } = MovementType.Elastic;

        /// <summary>
        ///     The size, in pixels, of the (bidirectional) cache extent for virtualization. Auto-derived from
        ///     the viewport size when not set.
        /// </summary>
        public float? VirtualizationCacheExtent { get; set; }

        public override State CreateState()
        {
            return new ScrollGridState();
        }

        public override RenderObject CreateRenderObject(BuildContext context, IState state)
        {
            return new RenderSliverGrid((ISliverGridState) state);
        }

        // See ScrollList.GetDiagnosticInfo: the logical count off the widget, never the built window.
        public override string GetDiagnosticInfo()
        {
            var count = ItemCount ?? Children?.Count ?? 0;
            return count == 1 ? "1 item" : count + " items";
        }
    }

    // Reactive half of the grid. Like ScrollListState it feeds the render object the full logical shape
    // (ISliverGridState) while exposing only the visible States to the View (IMultiChildLayoutState, via
    // IScrollingListState so the ScrollList prefab's view binds to it). The eager/lazy child window bridge is
    // delegated to the shared VirtualizedChildren; only the eager CreateChildren wiring stays here.
    public class ScrollGridState : ViewState<ScrollGrid>, ISliverGridState, IScrollingListState, IScrollControllerExecutor
    {
        private readonly StateCollectionHolder _allChildren;
        private readonly Dictionary<Key, int> _childKeyToIndexMap = new();
        private readonly VirtualizedChildren _virtualized;

        [CanBeNull] private ScrollListView _view;

        public ScrollGridState()
        {
            _allChildren = CreateChildren(context =>
            {
                var children = Widget.Children;
                _childKeyToIndexMap.Clear();
                for (var i = 0; i < children.Count; i++)
                {
                    var key = children[i]?.Key;
                    if (key != null) _childKeyToIndexMap.Add(key, i);
                }

                return children;
            });

            _virtualized = new VirtualizedChildren(
                StateLifetime,
                new BuildContext(this, Context),
                () => Widget.ItemBuilder,
                ResolveEagerIndex);
        }

        // Eager-mode resolver injected into the shared bridge: maps a visible index to its built child State.
        private IState ResolveEagerIndex(int index)
        {
            var all = _allChildren.Value;
            return index < all.Length ? all[index] : null;
        }

        private bool IsLazy => Widget.ItemBuilder != null;

        [Atom]
        IState[] IMultiChildLayoutState.Children => _virtualized.VisibleChildren;

        [Atom] public bool UseMask => Widget.UseMask;

        [Atom] public MovementType MovementType => Widget.MovementType;

        [Atom] public ScrollController ScrollController { get; private set; }

        // Pixel offset, not NormalizedValue -- see ScrollController.PixelOffset for why the estimation-based
        // lazy windowing needs an absolute value that doesn't drift with the estimated content size.
        [Atom] public float ScrollPixelOffset => ScrollController.PixelOffset;

        [Atom] public IState[] AllChildren => IsLazy ? Array.Empty<IState>() : _allChildren.Value;

        [Atom] public int? ItemCount => Widget.ItemCount;

        [Atom] public Axis Axis => Widget.Axis;

        [Atom] public float? VirtualizationCacheExtent => Widget.VirtualizationCacheExtent;

        [Atom] public RectPadding Padding => Widget.Padding;

        // The grid delegate is rebuilt from the widget's column/cell knobs; [Atom] so it recomputes only when
        // those inputs change, not every layout pass.
        [Atom] public SliverGridDelegate GridDelegate => BuildGridDelegate();

        private SliverGridDelegate BuildGridDelegate()
        {
            if (Widget.CrossAxisCount.HasValue)
            {
                return new SliverGridDelegateWithFixedCrossAxisCount(
                    Widget.CrossAxisCount.Value,
                    Widget.MainAxisSpacing,
                    Widget.CrossAxisSpacing,
                    Widget.ChildAspectRatio,
                    Widget.MainAxisExtent);
            }

            return new SliverGridDelegateWithMaxCrossAxisExtent(
                Widget.MaxCrossAxisExtent!.Value,
                Widget.MainAxisSpacing,
                Widget.CrossAxisSpacing,
                Widget.ChildAspectRatio,
                Widget.MainAxisExtent);
        }

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

        public override WidgetViewReference View => WidgetViewReference.Resource("Layout/UniMob.ScrollList");

        // A scroll grid is a viewport: it fills the space its parent gives it and scrolls its content,
        // rather than shrink-wrapping to that content. In the new layout system PerformSizing already
        // returns constraints.Largest; this override makes the grid fill *legacy* parents too (which size
        // children via WidgetSize), matching ScrollGridFlow. Without it the legacy bridge would derive the
        // size from the render object's intrinsics -- 0 on the cross axis -- and the grid would collapse.
        public override WidgetSize CalculateSize() => WidgetSize.Stretched;

        void ISliverGridState.SetVisibleChildren(List<IndexedLayoutData> visibleChildren)
            => _virtualized.SetVisibleChildren(visibleChildren);

        IState[] ISliverGridState.RequestBuildWindow(int startIndexInclusive, int endIndexExclusive)
            => _virtualized.RequestBuildWindow(startIndexInclusive, endIndexExclusive);

        public override void InitState()
        {
            base.InitState();

            ValidateMode();

            ScrollController = Widget.ScrollController ?? new ScrollController(StateLifetime);
            ScrollController.Attach(this);

            StateLifetime.Register(() => ScrollController.Detach(this));
        }

        public override void DidUpdateWidget(ScrollGrid oldWidget)
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
                    "ScrollGrid cannot have both ItemBuilder and Children set -- use ItemBuilder+ItemCount " +
                    "for lazy building, or Children for eager building, not both.");

            if (hasBuilder && Widget.ItemCount == null)
                throw new InvalidOperationException("ScrollGrid.ItemCount must be set when ItemBuilder is provided.");

            if (!hasBuilder && Widget.ItemCount != null)
                throw new InvalidOperationException("ScrollGrid.ItemCount has no effect without ItemBuilder.");

            if (Widget.CrossAxisCount.HasValue == Widget.MaxCrossAxisExtent.HasValue)
                throw new InvalidOperationException(
                    "ScrollGrid requires exactly one of CrossAxisCount or MaxCrossAxisExtent.");

            if (Widget.ChildAspectRatio.HasValue && Widget.MainAxisExtent.HasValue)
                throw new InvalidOperationException(
                    "ScrollGrid cannot have both ChildAspectRatio and MainAxisExtent set -- pick one way to " +
                    "size cells along the scroll axis (or neither, to measure rows).");
        }

        bool IScrollControllerExecutor.ScrollTo(int index, float duration, ScrollToPosition position, Easing easing)
        {
            return _view?.ScrollTo(index, duration, position, easing) ?? false;
        }

        bool IScrollControllerExecutor.ScrollTo(Key key, float duration, ScrollToPosition position, Easing easing)
        {
            int index;

            if (IsLazy)
            {
                if (Widget.KeyToIndexResolver != null)
                {
                    var resolved = Widget.KeyToIndexResolver(key);
                    if (resolved == null) return false;
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

            return ((IScrollControllerExecutor) this).ScrollTo(index, duration, position, easing);
        }
    }
}
