using System.Collections.Generic;
using JetBrains.Annotations;
using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Layout.Internal.Views;
using UnityEngine.UI;
using ScrollListView = UniMob.UI.Layout.Internal.Views.ScrollListView;
using Vector2 = UnityEngine.Vector2;

namespace UniMob.UI.Layout
{
    public class ScrollList : StatefulWidget
    {
        public List<Widget> Children { get; set; } = new();
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


    // The state implements both the generic interface for the View (IMultiChildLayoutState)
    // and our specific interface for the RenderObject (IScrollingListState).
    // The reason is that the RenderObject needs the entire children collection (visible AND invisible) to correctly
    // compute the layout, while the View only needs the visible children to render the UI.
    public class ScrollListState : ViewState<ScrollList>, ISliverState, IScrollingListState
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


        [CanBeNull] private ScrollListView _view;

        public ScrollListState()
        {
            _allChildren = CreateChildren(context =>
            {
                var children = Widget.Children;
                _childKeyToIndexMap.Clear();
                for (var i = 0; i < children.Count; i++)
                {
                    var key = children[i].Key;
                    if (key != null) _childKeyToIndexMap.Add(key, i);
                }

                return children;
            });

            _visibleChildren = Atom.Computed(StateLifetime, () =>
            {
                var indices = _visibleIndices.Value;
                var all = _allChildren.Value;
                var visible = new IState[indices.Count];

                for (var i = 0; i < indices.Count; i++)
                {
                    var index = indices[i];
                    if (index < all.Length) visible[i] = all[index];
                }

                return visible;
            });
        }

        [Atom]
        IState[] IMultiChildLayoutState.Children => _visibleChildren.Value;

        [Atom]
        public bool UseMask => Widget.UseMask;

        [Atom]
        public MovementType MovementType => Widget.MovementType;

        [Atom] public ScrollController ScrollController { get; private set; }
        [Atom] public Vector2 ViewportSize { get; set; }

        [Atom] public float NormalizedScrollOffset => ScrollController.NormalizedValue;

        [Atom]
        public IState[] AllChildren => _allChildren.Value;

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

            // Reading _visibleIndices.Value here would normally subscribe whichever atom is currently
            // evaluating (this is invoked from inside the ScrollList's own layout computation) as a
            // watcher of _visibleIndices -- which we're also about to write to below. That would make the
            // layout atom depend on a value it writes itself. Keep the read inside NoWatch too.
            using (Atom.NoWatch)
            {
                var current = _visibleIndices.Value;
                if (IndicesEqual(current, scratch))
                {
                    // The visible window hasn't actually changed (e.g. a scroll delta that didn't cross
                    // an item boundary). Skip the write entirely to avoid invalidating downstream atoms
                    // (and the IState[] allocation that triggers) for no observable change.
                    return;
                }

                _visibleIndices.Value = scratch;

                // The old value is now orphaned from the atom; reuse it as the next scratch buffer.
                _visibleIndicesScratch = current;
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



        public override void InitState()
        {
            base.InitState();

            // Use the provided controller or create a new one.
            ScrollController = Widget.ScrollController ?? new ScrollController(StateLifetime);
        }

        public override void DidUpdateWidget(ScrollList oldWidget)
        {
            base.DidUpdateWidget(oldWidget);

            if (Widget.ScrollController != null && Widget.ScrollController != ScrollController)
                ScrollController = Widget.ScrollController;
        }


        public bool ScrollTo(int index)
        {
            return ScrollTo(index, 0);
        }

        public bool ScrollTo(Key key)
        {
            return ScrollTo(key, 0);
        }

        public bool ScrollTo(int index, float duration, ScrollToPosition? position = null, Easing easing = null)
        {
            return _view?.ScrollTo(index, duration, position ?? ScrollToPosition.Start, easing ?? Ease.InOutCirc) ??
                   false;
        }


        public bool ScrollTo(Key key, float duration, ScrollToPosition? position = null, Easing easing = null)
        {
            if (!_childKeyToIndexMap.TryGetValue(key, out var index))
                return false;

            return ScrollTo(index, duration, position, easing);
        }
    }
}