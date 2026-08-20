using System;
using System.Collections.Generic;
using UniMob.UI.Internal;
using UniMob.UI.Rendering;

namespace UniMob.UI.Internal
{
    // The reactive bridge shared by the virtualized scrollables (ScrollList and ScrollGrid). A sliver
    // render object works in an imperative, index-driven layout pass while the View reads a computed
    // [Atom] of the *visible* States -- this class straddles that impedance mismatch for both widgets so
    // the fiddly NoWatch/atom discipline lives in exactly one place. The full architecture is documented
    // at the top of RenderSliverList.cs; the hazards specific to this bridge are called out inline below.
    //
    // Scope: it owns the LAZY building (ItemBuilder mode) and the visible-index tracking. The eager
    // ("Children") path stays in the owning state, because building those goes through State.CreateChildren
    // (a protected State method this helper can't call); the owner injects how to resolve an eager index to
    // a State via `resolveEagerIndex`.
    internal sealed class VirtualizedChildren
    {
        private readonly BuildContext _itemBuildContext;

        // Returns the owner's *current* Widget.ItemBuilder. Called inside tracked atom pulls (not captured
        // once) so a widget swap that changes the builder invalidates the built window -- see BuildWindow.
        private readonly Func<IndexedWidgetBuilder?> _itemBuilder;

        // Owner-provided eager resolver (index -> State from the owner's CreateChildren collection). Only
        // consulted in eager mode; in lazy mode the visible set resolves from _builtStates below.
        private readonly Func<int, IState?> _resolveEagerIndex;

        // Cache of currently-built items by logical index. Deliberately NOT routed through
        // StateCollectionHolder (its reconciler disposes anything absent from the list it's given, so a
        // windowed subset would rebuild every item each time the window shifts). A plain field, not an Atom:
        // mutated within a layout pass.
        private readonly Dictionary<int, State> _builtStates = new();

        // Every index ever built, kept even after eviction, so ScrollTo(Key) can still resolve keys for
        // items that scrolled out of the build window (unless the owner supplies its own key resolver).
        private readonly Dictionary<Key, int> _seenKeyToIndex = new();

        // Scratch buffer for BuildWindow's eviction pass, reused to avoid per-call allocation.
        private readonly List<int> _evictionScratch = new();

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

        public VirtualizedChildren(
            Lifetime lifetime,
            BuildContext itemBuildContext,
            Func<IndexedWidgetBuilder?> itemBuilder,
            Func<int, IState?> resolveEagerIndex
        )
        {
            _itemBuildContext = itemBuildContext;
            _itemBuilder = itemBuilder;
            _resolveEagerIndex = resolveEagerIndex;

            _builtWindow = Atom.Computed(lifetime, BuildWindow);
            _visibleChildren = Atom.Computed(lifetime, ComputeVisibleChildren);

            lifetime.Register(DeactivateBuiltStates);
        }

        /// <summary>The visible child States, in the order the render object reported them. Reactive.</summary>
        public IState[] VisibleChildren => _visibleChildren.Value;

        /// <summary>Resolves a key seen at least once during building to its index; used by ScrollTo(Key).</summary>
        public bool TryResolveSeenKey(Key key, out int index) =>
            _seenKeyToIndex.TryGetValue(key, out index);

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
                // Both resolvers answer null for an index the owner no longer has, which the visible
                // index list is written to rule out; layout dereferences what it is handed.
                visible[i] = (
                    lazy
                        ? _builtStates.GetValueOrDefault(indices[i])
                        : _resolveEagerIndex(indices[i])
                )!;
            }

            return visible;
        }

        // Pull body of _builtWindow: evicts indices now outside the window and (re)builds ItemBuilder's
        // widgets for it. Reads _buildWindowRange.Value directly (not via parameters) so it runs as tracked
        // atom evaluation -- see _builtWindow's field doc.
        private IState[] BuildWindow()
        {
            var (startIndexInclusive, endIndexExclusive) = _buildWindowRange.Value;

            _evictionScratch.Clear();
            foreach (var index in _builtStates.Keys)
            {
                if (index < startIndexInclusive || index >= endIndexExclusive)
                    _evictionScratch.Add(index);
            }

            // Read the current builder OUTSIDE NoWatch so the widget is tracked (a widget swap must rebuild).
            var itemBuilder = _itemBuilder();

            // Only reconciliation (UpdateChild/DeactivateChild) goes in NoWatch -- it asserts it isn't inside
            // a tracked scope, and this method IS _builtWindow's tracked pull. ItemBuilder must stay OUTSIDE
            // NoWatch (see the build loop below): it reads arbitrary reactive state (e.g. a ViewModel [Atom]
            // "IsSelected" flag), and that read must be tracked as a dependency of _builtWindow so a later
            // change re-runs the current window even if its index range never moves.
            using (Atom.NoWatch)
            {
                foreach (var index in _evictionScratch)
                {
                    StateUtilities.DeactivateChild(_builtStates[index]);
                    _builtStates.Remove(index);
                }
            }

            // Re-run ItemBuilder for every index in the window each time this pull fires (not just
            // newly-entering ones) -- like the eager Children path re-diffing its whole list. UpdateChild is
            // cheap when unchanged (returns the same State) and rebuilds just the slots whose Key/Type moved.
            // No builder is the eager path, which reaches here only with an empty window.
            if (itemBuilder != null)
            {
                for (var index = startIndexInclusive; index < endIndexExclusive; index++)
                {
                    var widget = itemBuilder(_itemBuildContext, index);

                    using (Atom.NoWatch)
                    {
                        var built = StateUtilities.UpdateChild(
                            _itemBuildContext,
                            _builtStates.GetValueOrDefault(index),
                            widget
                        );
                        // Never null: IndexedWidgetBuilder returns a widget, and UpdateChild answers
                        // null only for a null one.
                        _builtStates[index] = built!;

                        if (built!.Key != null)
                            _seenKeyToIndex[built.Key] = index;
                    }
                }
            }

            var result = new IState[endIndexExclusive - startIndexInclusive];
            for (var index = startIndexInclusive; index < endIndexExclusive; index++)
                result[index - startIndexInclusive] = _builtStates[index];

            return result;
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
