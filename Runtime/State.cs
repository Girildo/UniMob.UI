#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !UNIMOB_DISABLE_REBUILD_RATE_LIMITER
#define UNIMOB_ENABLE_REBUILD_RATE_LIMITER
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using UniMob.UI.Internal;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI
{
    [System.Diagnostics.DebuggerDisplay("{ToDiagnosticString()}")]
    public abstract class State : IState, IDisposable, ILifetimeScope
    {
        private readonly MutableAtom<LayoutConstraints?> _explicitConstraints = Atom.Value(default(LayoutConstraints?));
        private readonly Atom<(Vector2 renderSize, int version)> _trackedLayoutPerformer;
        private readonly Atom<Vector2> _trackedSize;

        private readonly MutableBuildContext _context;

        private LifetimeController _stateLifetimeController;
        private RenderObject _renderObject;

        private int _renderVersion = int.MinValue;

        public virtual RenderObject RenderObject => _renderObject;
        public BuildContext Context => _context;

        internal Widget RawWidget { get; private set; }
        Widget IState.RawWidget => RawWidget;

        public LayoutConstraints Constraints =>
            _explicitConstraints.Value ?? _context.Parent?.State?.Constraints ?? default;

        public abstract IViewState InnerViewState { get; }

        public abstract WidgetSize Size { get; }

        public Key Key => RawWidget.Key;

        Lifetime ILifetimeScope.Lifetime => StateLifetime;

        public Lifetime StateLifetime
        {
            get
            {
                if (_stateLifetimeController == null)
                {
                    _stateLifetimeController = new LifetimeController();
                }

                return _stateLifetimeController.Lifetime;
            }
        }

        protected State()
        {
            _context = new MutableBuildContext(this, null);
            _trackedLayoutPerformer = CreateTrackedLayout();
            _trackedSize = CreateTrackedSize();
        }

        internal virtual void Update(Widget widget)
        {
            RawWidget = widget;

            // Force recompute layout on Widget modifications.
            _trackedLayoutPerformer.Invalidate();
        }

        internal virtual void UpdateConstraints(LayoutConstraints constraints)
        {
            using (Atom.NoWatch)
            {
                _explicitConstraints.Value = constraints;
            }
        }

        internal void Mount(BuildContext context)
        {
            if (Context.Parent != null)
                throw new InvalidOperationException("State already mounted");

            _context.SetParent(context);
        }

        
        internal virtual void InitRenderObject()
        {
            _renderObject = RawWidget.CreateRenderObject(Context, this);
        }

        public virtual void InitState()
        {
        }

        public virtual void Dispose()
        {
            _stateLifetimeController?.Dispose();
        }

        void IState.UpdateConstraints(LayoutConstraints constraints) => UpdateConstraints(constraints);

        Vector2 IState.WatchedPerformLayout()
        {
            if(this.StateLifetime.IsDisposed)
                return Vector2.zero;
            var renderData = _trackedLayoutPerformer.Get();
            return renderData.renderSize;
        }

        Vector2 IState.WatchedSize()
        {
            if (this.StateLifetime.IsDisposed)
                return Vector2.zero;
            return _trackedSize.Get();
        }

        private Atom<(Vector2 renderSize, int version)> CreateTrackedLayout()
        {
            return Atom.Computed(StateLifetime, () =>
            {
                if(StateLifetime.IsDisposed)
                    return (Vector2.zero, _renderVersion);

                // PerformLayout() implicitly uses many [Atom] so trackedLayoutPerformer will be auto recomputed.
                RenderObject.PerformLayoutImmediate(Constraints);

                // Also recompute layout on Constraints modifications.
                _ = Constraints;

                var renderSize = RenderObject.Size;

                // The PerformLayout() was done and we need all subscribers to be invalidated,
                // so we always return a new number.
                return (renderSize, _renderVersion = ((_renderVersion + 1) % int.MaxValue));
            });
        }

        // Mirrors _trackedLayoutPerformer's renderSize, but as a plain Vector2-valued computed atom.
        // Unlike _trackedLayoutPerformer (which always bumps a version number so View-layer consumers
        // refresh on every layout pass, including pure repositioning), this atom goes through UniMob's
        // normal equality-based propagation cutoff: a subscriber here only gets invalidated when the
        // Size itself actually changes. Used by RenderObject.LayoutChild so a parent's sizing pass
        // doesn't get dragged into re-running every time a child's subtree merely repositions itself
        // (e.g. a nested ScrollList scrolling).
        private Atom<Vector2> CreateTrackedSize()
        {
            return Atom.Computed(StateLifetime, () => _trackedLayoutPerformer.Get().renderSize);
        }

        internal static StateHolder<TState> Create<TWidget, TState>(
            Lifetime lifetime,
            BuildContext context,
            WidgetBuilder<TWidget> builder)
            where TWidget : Widget
            where TState : class, IState
        {
            return new StateHolder<TWidget, TState>(lifetime, context, builder);
        }

        internal static StateCollectionHolder CreateList(
            Lifetime lifetime,
            BuildContext context,
            Func<BuildContext,
                List<Widget>> builder)
        {
            return new StateCollectionHolder(lifetime, context, builder);
        }

        /// <summary>
        /// Schedules a callback to be invoked on the next frame, after atom updates.
        /// The callback will only be invoked if the state is still alive.
        /// </summary>
        protected void AddPostFrameCallback(Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            Zone.Current.NextFrame(()=>
                {
                    if(!this.StateLifetime.IsDisposed)
                        callback();
                }
            );
        }

        public virtual string GetDiagnosticInfo() => null;
    }

    public class StateCollectionHolder
    {
        private readonly BuildContext _context;
        private readonly Func<BuildContext, List<Widget>> _builder;
        private readonly Atom<IState[]> _statesAtom;

        private State[] _states = new State[0];

        public IState[] Value => _statesAtom.Value;

        public StateCollectionHolder(Lifetime lifetime, BuildContext context, Func<BuildContext, List<Widget>> builder)
        {
            _context = context;
            _builder = builder;
            _statesAtom = Atom.Computed(lifetime, ComputeStates, debugName: "StateCollectionHolder._statesAtom");

            lifetime.Register(DeactivateStates);
        }

        private State[] ComputeStates()
        {
            var newWidgets = _builder.Invoke(_context);
            using (Atom.NoWatch)
            {
                _states = StateUtilities.UpdateChildren(_context, _states, newWidgets);
            }

            return _states.ToArray();
        }

        private void DeactivateStates()
        {
            foreach (var state in _states)
            {
                StateUtilities.DeactivateChild(state);
            }
        }
    }

    // ReSharper disable once InconsistentNaming
    public interface StateHolder
    {
        IState Value { get; }
    }

    // ReSharper disable once InconsistentNaming
    public interface StateHolder<out TState> : StateHolder
    {
        new TState Value { get; }
    }

    public sealed class StateHolder<TWidget, TState> : StateHolder<TState>
        where TWidget : Widget
        where TState : class, IState
    {
        private readonly BuildContext _context;
        private readonly WidgetBuilder<TWidget> _builder;
        private readonly Atom<TState> _stateAtom;

#if UNIMOB_ENABLE_REBUILD_RATE_LIMITER
        private UniMobRebuildRateLimiter _rebuildRateLimiter;
#endif

        private State _state;

        public StateHolder(Lifetime lifetime, BuildContext context, WidgetBuilder<TWidget> builder)
        {
            _context = context;
            _builder = builder;
            _stateAtom = Atom.Computed(lifetime, ComputeState, debugName: "StateHolder._statesAtom");

#if UNIMOB_ENABLE_REBUILD_RATE_LIMITER
            _rebuildRateLimiter = new UniMobRebuildRateLimiter(context);
#endif

            lifetime.Register(DeactivateState);
        }

        IState StateHolder.Value => _stateAtom.Value;
        TState StateHolder<TState>.Value => _stateAtom.Value;

        private TState ComputeState()
        {
            var newWidget = _builder(_context);

#if UNIMOB_ENABLE_REBUILD_RATE_LIMITER
            _rebuildRateLimiter.TrackRebuild(newWidget);
#endif

            using (Atom.NoWatch)
            {
                _state = StateUtilities.UpdateChild(_context, _state, newWidget);
            }

            return _state as TState;
        }

        private void DeactivateState()
        {
            if (_state != null)
            {
                StateUtilities.DeactivateChild(_state);
            }
        }
    }
}