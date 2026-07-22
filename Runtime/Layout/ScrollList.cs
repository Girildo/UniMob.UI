using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UniMob.UI.Internal;
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
            return new RenderSliverList((ISliverState) state);
        }
    }


    // The reactive half of the virtualized list -- the full architecture & pitfalls are documented at the top of
    // RenderSliverList.cs; this file only adds the reactive-bridge hazards specific to it.
    //
    // It implements two interfaces on purpose: ISliverState/IScrollingListState feed the RenderObject the *full*
    // logical shape (it needs every item, visible or not, to compute layout), while IMultiChildLayoutState exposes
    // only the *visible* States to the View. The imperative layout pass writes indices while the View reads a
    // computed [Atom] -- that impedance mismatch is why this class exists, and why the NoWatch scoping below is fiddly.
    public class ScrollListState : ViewState<ScrollList>, ISliverState, IScrollingListState, IScrollControllerExecutor
    {

        private readonly StateCollectionHolder _allChildren;
        private readonly Dictionary<Key, int> _childKeyToIndexMap = new();
        private readonly Atom<IState[]> _visibleChildren;

        // A reactive atom holding the INDICES of the visible children.
        private readonly MutableAtom<List<int>> _visibleIndices = Atom.Value(new List<int>());

        // The buffer not currently referenced by _visibleIndices.Value, reused to build the next candidate
        // set without allocating. Ping-pongs with whatever _visibleIndices.Value held previously.
        private List<int> _visibleIndicesScratch = new();
        public float Spacing => this.Widget.Spacing;

        // --- Lazy building (ItemBuilder/ItemCount mode) ---
        // Cache of currently-built items by logical index. Deliberately NOT routed through StateCollectionHolder
        // (its reconciler disposes anything absent from the list it's given, so a windowed subset would rebuild
        // every item each time the window shifts). A plain field, not an Atom: mutated within a layout pass.
        private readonly Dictionary<int, State> _builtStates = new();

        // Every index ever built, kept even after eviction, so ScrollTo(Key) can still resolve keys for items
        // that scrolled out of the build window (unless Widget.KeyToIndexResolver is supplied instead).
        private readonly Dictionary<Key, int> _seenKeyToIndex = new();

        // Scratch buffer for BuildWindow's eviction pass, reused to avoid per-call allocation.
        private readonly List<int> _evictionScratch = new();

        // The [start, end) window RenderSliverList currently wants built. A MutableAtom, not a plain field, so
        // re-writing the SAME range (the common case: RequestBuildWindow fires every scroll tick, but crossing an
        // item boundary is rare) is a reactive no-op -- MutableAtom's setter skips invalidation on an equal value,
        // which is what leaves _builtWindow untouched across pure-scroll frames with no manual bookkeeping here.
        private readonly MutableAtom<(int start, int end)> _buildWindowRange = Atom.Value((0, 0));

        // Built States for _buildWindowRange's current window. A ComputedAtom, not a method, so UniMob's staleness
        // tracking re-runs its pull body (BuildWindow) exactly when it should: when the range changes OR when
        // anything ItemBuilder transitively reads (e.g. a ViewModel's [Atom] selection flag) changes, and skips it
        // otherwise -- no caller has to reason about *why* the window was requested again.
        private readonly Atom<IState[]> _builtWindow;

        // Shared BuildContext for every lazily-built item -- mirrors CreateChildren's `new BuildContext(this, Context)`,
        // just cached once since, unlike per-widget-instance children, there's no per-item context to derive here.
        private BuildContext _itemBuildContext;

        private bool IsLazy => Widget.ItemBuilder != null;

        [CanBeNull] private ScrollListView _view;

        public ScrollListState()
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

            _itemBuildContext = new BuildContext(this, Context);
            _builtWindow = Atom.Computed(StateLifetime, BuildWindow);

            _visibleChildren = Atom.Computed(StateLifetime, () =>
            {
                var indices = _visibleIndices.Value;
                var visible = new IState[indices.Count];

                if (IsLazy)
                {
                    // _builtStates is a plain field (untracked here) yet safe: it's only mutated in
                    // RequestBuildWindow, which runs before SetVisibleChildren writes _visibleIndices in the same
                    // pass -- so by the time this recomputes, _builtStates already reflects the current pass.
                    for (var i = 0; i < indices.Count; i++)
                    {
                        _builtStates.TryGetValue(indices[i], out var state);
                        visible[i] = state;
                    }
                }
                else
                {
                    var all = _allChildren.Value;
                    for (var i = 0; i < indices.Count; i++)
                    {
                        var index = indices[i];
                        if (index < all.Length) visible[i] = all[index];
                    }
                }

                return visible;
            });

            StateLifetime.Register(DeactivateBuiltStates);
        }

        private void DeactivateBuiltStates()
        {
            foreach (var state in _builtStates.Values)
            {
                StateUtilities.DeactivateChild(state);
            }

            _builtStates.Clear();
        }

        [Atom]
        IState[] IMultiChildLayoutState.Children => _visibleChildren.Value;

        [Atom]
        public bool UseMask => Widget.UseMask;

        [Atom]
        public MovementType MovementType => Widget.MovementType;

        [Atom] public ScrollController ScrollController { get; private set; }
        [Atom] public Vector2 ViewportSize { get; set; }

        // Pixel offset, not NormalizedValue -- see ScrollController.PixelOffset's doc for why RenderSliverList's
        // estimation-based lazy windowing needs an absolute value that doesn't drift when its own estimated
        // total content size changes between layout passes.
        [Atom] public float ScrollPixelOffset => ScrollController.PixelOffset;

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

        public override WidgetViewReference View => WidgetViewReference.Resource("Layout/UniMob.ScrollList");

        float? ISliverState.VirtualizationCacheExtent => VirtualizationCacheExtent;

        void ISliverState.SetVisibleChildren(List<IndexedLayoutData> visibleChildren)
        {
            var scratch = _visibleIndicesScratch;
            scratch.Clear();
            for (var i = 0; i < visibleChildren.Count; i++) scratch.Add(visibleChildren[i].ChildIndex);

            // NoWatch: this runs inside the list's own layout computation, so reading _visibleIndices here would
            // subscribe that atom to a value it's about to write -- a self-dependency.
            using (Atom.NoWatch)
            {
                var current = _visibleIndices.Value;
                // Unchanged window (a scroll delta that didn't cross an item boundary): skip the write so we don't
                // invalidate downstream atoms (and allocate a fresh IState[]) for no observable change.
                if (IndicesEqual(current, scratch)) return;

                _visibleIndices.Value = scratch;
                _visibleIndicesScratch = current; // old value is orphaned; reuse it as the next scratch buffer
            }
        }

        private static bool IndicesEqual(List<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }

            return true;
        }

        IState[] ISliverState.RequestBuildWindow(int startIndexInclusive, int endIndexExclusive)
        {
            using (Atom.NoWatch)
            {
                _buildWindowRange.Value = (startIndexInclusive, endIndexExclusive);
            }
            return _builtWindow.Value;
        }

        // Pull body of _builtWindow: evicts indices now outside the window and (re)builds ItemBuilder's widgets
        // for it. Reads _buildWindowRange.Value directly (not via parameters) so it runs as tracked atom
        // evaluation -- see _builtWindow's field doc.
        private IState[] BuildWindow()
        {
            var (startIndexInclusive, endIndexExclusive) = _buildWindowRange.Value;

            _evictionScratch.Clear();
            foreach (var index in _builtStates.Keys)
            {
                if (index < startIndexInclusive || index >= endIndexExclusive)
                    _evictionScratch.Add(index);
            }

            // Only reconciliation (UpdateChild/DeactivateChild) goes in NoWatch -- it asserts it isn't inside a
            // tracked scope, and this method IS _builtWindow's tracked pull. Widget.ItemBuilder must stay OUTSIDE
            // NoWatch (see the build loop below): it reads arbitrary reactive state (e.g. a ViewModel [Atom]
            // "IsSelected" flag), and that read must be tracked as a dependency of _builtWindow so a later change
            // re-runs the current window even if its index range never moves. (This split regressed once during
            // development -- see ScrollListWindowTests.)
            using (Atom.NoWatch)
            {
                foreach (var index in _evictionScratch)
                {
                    StateUtilities.DeactivateChild(_builtStates[index]);
                    _builtStates.Remove(index);
                }
            }

            // Re-run ItemBuilder for every index in the window each time this pull fires (not just newly-entering
            // ones) -- like the eager Children path re-diffing its whole list. UpdateChild is cheap when unchanged
            // (returns the same State) and rebuilds just the slots whose Key/Type no longer match.
            for (var index = startIndexInclusive; index < endIndexExclusive; index++)
            {
                var widget = Widget.ItemBuilder(_itemBuildContext, index);

                using (Atom.NoWatch)
                {
                    var built = StateUtilities.UpdateChild(_itemBuildContext, _builtStates.GetValueOrDefault(index), widget);
                    _builtStates[index] = built;

                    if (built.Key != null) _seenKeyToIndex[built.Key] = index;
                }
            }

            var result = new IState[endIndexExclusive - startIndexInclusive];
            for (var index = startIndexInclusive; index < endIndexExclusive; index++)
                result[index - startIndexInclusive] = _builtStates[index];

            return result;
        }

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
                    "ScrollList cannot have both ItemBuilder and Children set -- use ItemBuilder+ItemCount " +
                    "for lazy building, or Children for eager building, not both.");

            if (hasBuilder && Widget.ItemCount == null)
                throw new InvalidOperationException("ScrollList.ItemCount must be set when ItemBuilder is provided.");

            if (!hasBuilder && Widget.ItemCount != null)
                throw new InvalidOperationException("ScrollList.ItemCount has no effect without ItemBuilder.");
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
                else if (!_seenKeyToIndex.TryGetValue(key, out index))
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