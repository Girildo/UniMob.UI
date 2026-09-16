using System;
using System.Collections.Generic;
using UniMob.UI.Diagnostics;
using UniMob.UI.Rendering;

namespace UniMob.UI.Internal
{
    // The reactive bridge shared by the virtualized scrollables (ScrollList and ScrollGrid). A sliver
    // render object works in an imperative, index-driven layout pass while the View reads a computed
    // [Atom] of the *visible* States -- this class straddles that impedance mismatch for both widgets so
    // the fiddly NoWatch/atom discipline lives in exactly one place. The full architecture is documented
    // at the top of RenderSliverList.cs; the hazards specific to this bridge are called out inline below.
    //
    // Scope: every way a child is addressed. It builds the lazy window (ItemBuilder mode), tracks the
    // visible indices, indexes the eager children's keys, and answers both index-by-key questions. The
    // eager States go through State.CreateChildren, a protected State method this helper cannot call,
    // so the owner hands that capability in as a delegate and the holder is built here.
    internal sealed class VirtualizedChildren
    {
        // The scrollable this addresses the children of. Named in a fault report, so the author reads
        // about the widget they wrote.
        private readonly IState _owner;

        private readonly BuildContext _itemBuildContext;

        // Returns the owner's *current* Widget.ItemBuilder. Called inside tracked atom pulls (not captured
        // once) so a widget swap that changes the builder invalidates the built window -- see BuildWindow.
        private readonly Func<IndexedWidgetBuilder?> _itemBuilder;

        // Returns the owner's *current* Widget.Children, for the same reason _itemBuilder is a delegate.
        private readonly Func<List<Widget>> _eagerWidgets;

        // The eagerly-built children. Only consulted in eager mode; in lazy mode the visible set
        // resolves from _builtStates below.
        private readonly StateCollectionHolder _eagerChildren;

        // Index of the eager children's keys, rebuilt by AdoptEagerChildren on every eager build.
        private readonly Dictionary<Key, int> _eagerKeyToIndex = new();

        // Cache of currently-built items by logical index. Deliberately NOT routed through
        // StateCollectionHolder (its reconciler disposes anything absent from the list it's given, so a
        // windowed subset would rebuild every item each time the window shifts). A plain field, not an Atom:
        // mutated within a layout pass.
        private readonly Dictionary<int, State> _builtStates = new();

        // Every index ever built, kept even after eviction, so ScrollTo(Key) can still resolve keys for
        // items that scrolled out of the build window (unless the owner supplies its own key resolver).
        private readonly Dictionary<Key, int> _seenKeyToIndex = new();

        // Scratch buffers for BuildWindow, reused to avoid per-call allocation. The widgets the builder
        // returned for the window, the State each of them claimed, and the built States not yet claimed,
        // split by whether they can be found by key or only by the slot they were built at.
        private readonly List<Widget> _windowWidgetsScratch = new();
        private readonly List<State?> _claimedScratch = new();
        private readonly Dictionary<Key, State> _unclaimedByKeyScratch = new();
        private readonly Dictionary<int, State> _unclaimedBySlotScratch = new();

        // The [start, end) window the render object currently wants built. A MutableAtom, not a plain field,
        // so re-writing the SAME range (the common case: RequestBuildWindow fires every scroll tick, but
        // crossing an item boundary is rare) is a reactive no-op -- MutableAtom's setter skips invalidation
        // on an equal value, leaving _builtWindow untouched across pure-scroll frames with no bookkeeping.
        private readonly MutableAtom<(int start, int end)> _buildWindowRange = Atom.Value((0, 0));

        // Built States for _buildWindowRange's current window. A ComputedAtom, not a method, so UniMob's
        // staleness tracking re-runs its pull body (BuildWindow) exactly when it should: when the range
        // changes OR when anything ItemBuilder transitively reads (e.g. a ViewModel's [Atom] selection flag)
        // changes, and skips it otherwise.
        private readonly Atom<IState[]> _builtWindow;

        // A reactive atom holding the INDICES of the visible children (written by SetVisibleChildren).
        private readonly MutableAtom<List<int>> _visibleIndices = Atom.Value(new List<int>());

        // The buffer not currently referenced by _visibleIndices.Value, reused to build the next candidate
        // set without allocating. Ping-pongs with whatever _visibleIndices.Value held previously.
        private List<int> _visibleIndicesScratch = new();

        // Computed visible States: reads _visibleIndices and resolves each index to a State (from
        // _builtStates in lazy mode, or the owner's eager collection otherwise).
        private readonly Atom<IState[]> _visibleChildren;

        /// <param name="createChildren">
        ///     The owner's <c>State.CreateChildren</c>, which is protected and so cannot be reached
        ///     from here. It is handed <see cref="AdoptEagerChildren" /> and builds lazily, on the
        ///     first pull of the holder it returns.
        /// </param>
        public VirtualizedChildren(
            Lifetime lifetime,
            IState owner,
            BuildContext itemBuildContext,
            Func<IndexedWidgetBuilder?> itemBuilder,
            Func<List<Widget>> eagerWidgets,
            Func<Func<BuildContext, List<Widget>>, StateCollectionHolder> createChildren
        )
        {
            _owner = owner;
            _itemBuildContext = itemBuildContext;
            _itemBuilder = itemBuilder;
            _eagerWidgets = eagerWidgets;
            _eagerChildren = createChildren(AdoptEagerChildren);

            _builtWindow = Atom.Computed(lifetime, BuildWindow);
            _visibleChildren = Atom.Computed(lifetime, ComputeVisibleChildren);

            lifetime.Register(DeactivateBuiltStates);
        }

        /// <summary>The visible child States, in the order the render object reported them. Reactive.</summary>
        public IState[] VisibleChildren => _visibleChildren.Value;

        /// <summary>The eagerly-built child States, in the order the widget lists them. Reactive.</summary>
        public IState[] EagerChildren => _eagerChildren.Value;

        /// <summary>
        ///     Rebuilds the eager key index from the owner's children and answers the children to
        ///     build. A key used twice is reported as a fault against the owner and nothing is built:
        ///     a scrollable whose keys do not identify its children cannot reconcile, scroll to a key,
        ///     or be trusted to show the right item. The widget's own list is never modified.
        /// </summary>
        private List<Widget> AdoptEagerChildren(BuildContext context)
        {
            var children = _eagerWidgets();

            _eagerKeyToIndex.Clear();

            for (var i = 0; i < children.Count; i++)
            {
                var key = children[i]?.Key;
                if (key == null)
                    continue;

                try
                {
                    _eagerKeyToIndex.Add(key, i);
                }
                catch (ArgumentException ex)
                {
                    var widgetName = _owner.RawWidget.GetType().Name;
                    UniMobError.Report(
                        new UniMobFault(
                            ex,
                            $"{widgetName}: duplicate child key detected. Each child of a {widgetName} "
                                + "must have a unique Key.",
                            _owner
                        )
                    );
                    return new List<Widget>();
                }
            }

            return children;
        }

        /// <summary>
        ///     Resolves <paramref name="key" /> to the index of the item carrying it, for
        ///     <c>ScrollTo(Key)</c>. In lazy mode <paramref name="resolver" /> answers when the owner
        ///     supplied one, so an item that has never been built is still reachable; otherwise only
        ///     keys built at least once resolve. In eager mode the children's own keys answer.
        /// </summary>
        public bool TryResolveIndex(Key key, Func<Key, int?>? resolver, out int index)
        {
            if (_itemBuilder() == null)
                return _eagerKeyToIndex.TryGetValue(key, out index);

            if (resolver == null)
                return _seenKeyToIndex.TryGetValue(key, out index);

            var resolved = resolver(key);
            index = resolved ?? 0;
            return resolved != null;
        }

        /// <summary>
        ///     Ensures indices in [startIndexInclusive, endIndexExclusive) are built (constructing
        ///     newly-entering ones and evicting ones that fell outside the window since the last call), and
        ///     returns their States in index order.
        /// </summary>
        public IState[] RequestBuildWindow(int startIndexInclusive, int endIndexExclusive)
        {
            using (Atom.NoWatch)
            {
                _buildWindowRange.Value = (startIndexInclusive, endIndexExclusive);
            }

            return _builtWindow.Value;
        }

        public void SetVisibleChildren(List<IndexedLayoutData> visibleChildren)
        {
            var scratch = _visibleIndicesScratch;
            scratch.Clear();
            for (var i = 0; i < visibleChildren.Count; i++)
                scratch.Add(visibleChildren[i].ChildIndex);

            // NoWatch: this runs inside the list's own layout computation, so reading _visibleIndices here
            // would subscribe that atom to a value it's about to write -- a self-dependency.
            using (Atom.NoWatch)
            {
                var current = _visibleIndices.Value;
                // Unchanged window (a scroll delta that didn't cross an item boundary): skip the write so we
                // don't invalidate downstream atoms (and allocate a fresh IState[]) for no observable change.
                if (IndicesEqual(current, scratch))
                    return;

                _visibleIndices.Value = scratch;
                _visibleIndicesScratch = current; // old value is orphaned; reuse it as the next scratch buffer
            }
        }

        private IState[] ComputeVisibleChildren()
        {
            var indices = _visibleIndices.Value;
            var visible = new IState[indices.Count];

            // Reading the builder tracks the owning widget, mirroring the original IsLazy read here.
            var lazy = _itemBuilder() != null;

            for (var i = 0; i < indices.Count; i++)
            {
                // _builtStates is a plain field (untracked here) yet safe: it's only mutated in BuildWindow,
                // which runs (via RequestBuildWindow) before SetVisibleChildren writes _visibleIndices in the
                // same pass -- so by the time this recomputes, _builtStates already reflects the current pass.
                // Both lookups answer null for an index the owner no longer has, which the visible
                // index list is written to rule out; layout dereferences what it is handed.
                visible[i] = (
                    lazy
                        ? _builtStates.GetValueOrDefault(indices[i])
                        : ResolveEagerIndex(indices[i])
                )!;
            }

            return visible;
        }

        private IState? ResolveEagerIndex(int index)
        {
            var all = _eagerChildren.Value;
            return index < all.Length ? all[index] : null;
        }

        // Pull body of _builtWindow: (re)builds ItemBuilder's widgets for the window and reconciles them
        // against the States built so far. Reads _buildWindowRange.Value directly (not via parameters) so
        // it runs as tracked atom evaluation -- see _builtWindow's field doc.
        private IState[] BuildWindow()
        {
            var (startIndexInclusive, endIndexExclusive) = _buildWindowRange.Value;

            // Read the current builder OUTSIDE NoWatch so the widget is tracked (a widget swap must rebuild).
            var itemBuilder = _itemBuilder();

            // Re-run ItemBuilder for every index in the window each time this pull fires (not just
            // newly-entering ones) -- like the eager Children path re-diffing its whole list. It must stay
            // OUTSIDE NoWatch: it reads arbitrary reactive state (e.g. a ViewModel [Atom] "IsSelected"
            // flag), and that read must be tracked as a dependency of _builtWindow so a later change
            // re-runs the current window even if its index range never moves. No builder is the eager
            // path, which reaches here only with an empty window.
            _windowWidgetsScratch.Clear();
            if (itemBuilder != null)
            {
                for (var index = startIndexInclusive; index < endIndexExclusive; index++)
                    _windowWidgetsScratch.Add(itemBuilder(_itemBuildContext, index));
            }

            // Only reconciliation (UpdateChild/DeactivateChild) goes in NoWatch -- it asserts it isn't inside
            // a tracked scope, and this method IS _builtWindow's tracked pull.
            using (Atom.NoWatch)
            {
                Reconcile(startIndexInclusive, _windowWidgetsScratch);
            }

            var result = new IState[endIndexExclusive - startIndexInclusive];
            for (var index = startIndexInclusive; index < endIndexExclusive; index++)
                result[index - startIndexInclusive] = _builtStates[index];

            return result;
        }

        // Matches the window's widgets to the built States and replaces _builtStates with the outcome.
        // A keyed widget claims the State carrying its key wherever that State was built, so an item
        // inserted, removed or reordered above the window shifts every slot without rebuilding any of
        // them. An unkeyed widget can only claim the State at its own slot. States nothing claimed are
        // deactivated: those that left the window, and those whose key or type no longer matches.
        private void Reconcile(int startIndexInclusive, List<Widget> widgets)
        {
            _unclaimedByKeyScratch.Clear();
            _unclaimedBySlotScratch.Clear();
            foreach (var pair in _builtStates)
            {
                // A key shared by two built States can only be found by slot: indexing the second
                // under the same key would shadow the first, and a State in neither map is never
                // deactivated.
                if (pair.Value.Key is { } key && _unclaimedByKeyScratch.TryAdd(key, pair.Value))
                    continue;

                _unclaimedBySlotScratch[pair.Key] = pair.Value;
            }

            _claimedScratch.Clear();
            for (var i = 0; i < widgets.Count; i++)
            {
                var widget = widgets[i];
                State? claimed = null;

                if (widget.Key is { } key)
                {
                    if (
                        _unclaimedByKeyScratch.TryGetValue(key, out var byKey)
                        && StateUtilities.CanUpdateWidget(byKey.RawWidget, widget)
                    )
                    {
                        _unclaimedByKeyScratch.Remove(key);
                        claimed = byKey;
                    }
                }
                else if (_unclaimedBySlotScratch.Remove(startIndexInclusive + i, out var bySlot))
                {
                    claimed = bySlot;
                }

                _claimedScratch.Add(claimed);
            }

            // Deactivated before anything is inflated: a GlobalKey handed from an outgoing State to an
            // incoming one is cleared by the outgoing State's disposal, which must not run after the
            // incoming one has registered itself.
            foreach (var state in _unclaimedByKeyScratch.Values)
                StateUtilities.DeactivateChild(state);
            foreach (var state in _unclaimedBySlotScratch.Values)
                StateUtilities.DeactivateChild(state);
            _unclaimedByKeyScratch.Clear();
            _unclaimedBySlotScratch.Clear();

            _builtStates.Clear();
            for (var i = 0; i < widgets.Count; i++)
            {
                var index = startIndexInclusive + i;

                // Never null: IndexedWidgetBuilder returns a widget, and UpdateChild answers null only
                // for a null one. An unkeyed claim whose type changed is replaced here, by UpdateChild.
                var built = StateUtilities.UpdateChild(
                    _itemBuildContext,
                    _claimedScratch[i],
                    widgets[i]
                )!;
                _builtStates[index] = built;

                if (built.Key != null)
                    _seenKeyToIndex[built.Key] = index;
            }

            _claimedScratch.Clear();
        }

        private void DeactivateBuiltStates()
        {
            foreach (var state in _builtStates.Values)
            {
                StateUtilities.DeactivateChild(state);
            }

            _builtStates.Clear();
        }

        private static bool IndicesEqual(List<int> a, List<int> b)
        {
            if (a.Count != b.Count)
                return false;
            for (var i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }
    }
}
