#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !UNIMOB_DISABLE_REBUILD_RATE_LIMITER
#define UNIMOB_ENABLE_REBUILD_RATE_LIMITER
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using UniMob.UI.Diagnostics;
using UniMob.UI.Internal;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.RenderObjects;
using UnityEngine;

namespace UniMob.UI
{
    [System.Diagnostics.DebuggerDisplay("{ToDiagnosticString()}")]
    public abstract class State : IState, IDisposable, ILifetimeScope
    {
        private readonly MutableBuildContext _context;

        private LifetimeController _stateLifetimeController;
        private RenderObject _renderObject;

        public RenderObject RenderObject => _renderObject;
        public BuildContext Context => _context;

        internal Widget RawWidget { get; private set; }
        Widget IState.RawWidget => RawWidget;

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
        }

        internal virtual void Update(Widget widget)
        {
            RawWidget = widget;

            // Widget properties are reached through plain accessors during sizing, so a replacement
            // widget instance is not something the layout pass can observe on its own. Say so
            // explicitly. Null before InitRenderObject, which runs later in InflateWidget.
            _renderObject?.InvalidateLayout();
        }

        internal void Mount(BuildContext context)
        {
            if (Context.Parent != null)
                throw new InvalidOperationException("State already mounted");

            _context.SetParent(context);
        }

        internal void InitRenderObject()
        {
            _renderObject = CreateOwnRenderObject();
        }

        /// <summary>
        ///     Builds the render object this state owns. Every state owns exactly one.
        /// </summary>
        /// <remarks>
        ///     Build-only states override this: they have no widget-supplied render object, but they do
        ///     have a child, so they own a proxy over it. That is what makes them an ordinary link in
        ///     the layout chain rather than a second driver of somebody else's render object.
        /// </remarks>
        internal virtual RenderObject CreateOwnRenderObject()
        {
            return RawWidget.CreateRenderObject(Context, this);
        }

        public virtual void InitState()
        {
        }

        public virtual void Dispose()
        {
            _stateLifetimeController?.Dispose();
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

        /// <summary>
        ///     A label describing this state, defaulting to whatever its widget says about itself.
        /// </summary>
        /// <remarks>
        ///     Override when the useful thing is runtime state rather than configuration, and compose
        ///     with <c>base</c> to keep both. The state wins over its widget because it is the only one
        ///     of the two that can see both.
        ///     <para>
        ///         Null-conditional because the window exists: <c>RawWidget</c> is not set until the
        ///         first Update, and <see cref="Key"/> has the same gap. A diagnostic is exactly the
        ///         thing that gets called during it.
        ///     </para>
        /// </remarks>
        public virtual string GetDiagnosticInfo() => RawWidget?.GetDiagnosticInfo();

        /// <summary>
        ///     What a debugger shows for this state: the node, then the constraints it was laid out
        ///     against and the size it answered with.
        /// </summary>
        /// <remarks>
        ///     Named by the DebuggerDisplay attribute on this class since it was written, and never
        ///     implemented, so the watch window has been showing an evaluation error where the widget's
        ///     identity should be.
        ///     <para>
        ///         Reads no atoms. A debugger evaluates expressions wherever execution happens to be
        ///         paused, which for this class is most often inside a layout computation, and a watch
        ///         window that quietly adds dependencies to the graph it is inspecting is worse than no
        ///         watch window.
        ///     </para>
        /// </remarks>
        public string ToDiagnosticString()
        {
            using (Atom.NoWatch)
            {
                var node = DiagnosticNode.Describe(this);

                if (_renderObject == null)
                {
                    return node;
                }

                var constraints = _renderObject.Constraints;
                return constraints.HasValue
                    ? $"{node}  {constraints.Value}  ->  {_renderObject.PeekSize()}"
                    : $"{node}  <not laid out>";
            }
        }
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