using System.Runtime.CompilerServices;
using UniMob.UI.Internal;

namespace UniMob.UI.Widgets
{
    using System;
    using System.Threading.Tasks;
    using UnityEngine;

    public abstract class Route : IBackActionOwner
    {
        private readonly RouteSettings _settings;
        private readonly TriggerStateMachine<ScreenState, ScreenEvent, Task> _machine;
        private readonly TaskCompletionSource<object> _popCompleter = new TaskCompletionSource<object>();
        private readonly TaskCompletionSource<object> _pushCompleter = new TaskCompletionSource<object>();
        private readonly TaskCompletionSource<object> _disposeCompleter = new TaskCompletionSource<object>();

        private Func<bool> _backAction;
        private object _popResult = null;

        protected Route(RouteSettings settings)
        {
            _settings = settings;
            _machine = BuildStateMachine();
        }

        public ScreenState ScreenState => _machine.State;

        public RouteModalType ModalType => _settings.ModalType;

        public Task<object> PopTask => _popCompleter.Task;

        public Task PushTask => _pushCompleter.Task;
        public Task DisposeTask => _disposeCompleter.Task;

        public string Key => _settings.Name;

        private TriggerStateMachine<ScreenState, ScreenEvent, Task> BuildStateMachine()
        {
            // Initializing → Created → Destroyed
            //                  ↓ ↑
            //                Resumed
            //                  ↓ ↑
            //                Focused
            var fsm = new TriggerStateMachine<ScreenState, ScreenEvent, Task>(ScreenState.Initializing);

            fsm.On(ScreenEvent.Create)
                .Allow(ScreenState.Initializing, ScreenState.Created, ExecTransition(OnCreate))
                .Allow(ScreenState.Created, ScreenState.Created);

            fsm.On(ScreenEvent.Resume)
                .Allow(ScreenState.Created, ScreenState.Resumed, ExecTransition(OnResume, ScreenEvent.Resume))
                .Allow(ScreenState.Resumed, ScreenState.Resumed);

            fsm.On(ScreenEvent.Focus)
                .Allow(ScreenState.Created, ScreenState.Resumed, ExecTransition(OnResume, ScreenEvent.Focus))
                .Allow(ScreenState.Resumed, ScreenState.Focused, ExecTransition(OnFocus))
                .Allow(ScreenState.Focused, ScreenState.Focused);

            fsm.On(ScreenEvent.Unfocus)
                .Allow(ScreenState.Focused, ScreenState.Resumed, ExecTransition(OnFocusLost))
                .Allow(ScreenState.Resumed, ScreenState.Resumed);

            fsm.On(ScreenEvent.Pause)
                .Allow(ScreenState.Focused, ScreenState.Resumed, ExecTransition(OnFocusLost, ScreenEvent.Pause))
                .Allow(ScreenState.Resumed, ScreenState.Created, ExecTransition(OnPause))
                .Allow(ScreenState.Created, ScreenState.Created);

            fsm.On(ScreenEvent.Destroy)
                .Allow(ScreenState.Focused, ScreenState.Resumed, ExecTransition(OnFocusLost, ScreenEvent.Destroy))
                .Allow(ScreenState.Resumed, ScreenState.Created, ExecTransition(OnPause, ScreenEvent.Destroy))
                .Allow(ScreenState.Created, ScreenState.Destroyed, ExecTransition(OnDestroy))
                .Allow(ScreenState.Destroyed, ScreenState.Destroyed);

            // Teardown is how a route ends when the tree it lives in is going away, as opposed to Destroy,
            // which is how it ends when navigation removed it. Every live state goes straight to Destroyed
            // rather than chaining through OnFocusLost and OnPause: those steps exist to hand the screen
            // back to whatever was underneath, and at teardown there is nothing underneath to hand it to.
            // Going direct is also what keeps this synchronous -- the chain through OnPause is what starts
            // a PageRoute's exit animation, and OnDestroy is what then waits for it, so a route torn down
            // this way has nothing to wait for and no animation to run.
            //
            // Destroyed is included, and carries the handler rather than being a bare self-transition,
            // because a route caught part-way through an ordinary destroy is already in that state while it
            // waits out its exit animation. That wait is abandoned when the tree disposes its lifetime, so
            // without a handler here the one route that most needs closing out would be the only one to
            // silently keep its callers waiting forever.
            fsm.On(ScreenEvent.Teardown)
                .Allow(ScreenState.Initializing, ScreenState.Destroyed, ExecTransition(OnTeardown))
                .Allow(ScreenState.Created, ScreenState.Destroyed, ExecTransition(OnTeardown))
                .Allow(ScreenState.Resumed, ScreenState.Destroyed, ExecTransition(OnTeardown))
                .Allow(ScreenState.Focused, ScreenState.Destroyed, ExecTransition(OnTeardown))
                .Allow(ScreenState.Destroyed, ScreenState.Destroyed, ExecTransition(OnTeardown));

            return fsm;
        }

        private Func<Task, Task> ExecTransition(Func<Task> handler, ScreenEvent? screenEvent = null) =>
            previous => ExecuteTransitionInternal(previous, handler, screenEvent);

        private async Task ExecuteTransitionInternal(Task previous, Func<Task> handler, ScreenEvent? screenEvent)
        {
            if (previous != null && !previous.IsCompleted)
            {
                await previous;
            }

            var current = handler();

            if (current != null && !current.IsCompleted)
            {
                await current;
            }

            if (screenEvent.HasValue)
            {
                var next = _machine.Trigger(screenEvent.Value);

                if (next != null && !next.IsCompleted)
                {
                    await next;
                }
            }
        }

        public virtual Task ApplyScreenEvent(ScreenEvent screenEvent)
        {
            if (_machine.CanTrigger(screenEvent))
            {
                return _machine.Trigger(screenEvent) ?? Task.CompletedTask;
            }

            Debug.LogErrorFormat("Cannot {0} scene {1} in {2} state", screenEvent, GetType().Name, ScreenState);
            return Task.CompletedTask;
        }

        public Task Initialize()
        {
            Zone.Current.NextFrame(() => _pushCompleter.SetResult(null));

            return OnInitialize() ?? Task.CompletedTask;
        }

        public virtual void Dispose()
        {
            Zone.Current.NextFrame(() => _disposeCompleter.SetResult(null));
        }

        protected virtual Task OnInitialize() => Task.CompletedTask;

        protected virtual Task OnCreate() => Task.CompletedTask;

        protected virtual Task OnPause() => Task.CompletedTask;

        protected virtual Task OnResume() => Task.CompletedTask;

        protected virtual Task OnFocus() => Task.CompletedTask;

        protected virtual Task OnFocusLost() => Task.CompletedTask;

        protected virtual Task OnDestroy()
        {
            CompletePop();

            return Task.CompletedTask;
        }

        /// <summary>
        ///     Ends the route because the tree it lives in is going away, rather than because navigation
        ///     removed it.
        /// </summary>
        /// <remarks>
        ///     Deliberately not <see cref="OnDestroy"/>: a subclass overrides that to finish a transition,
        ///     and there is no transition to finish here. <see cref="PageRoute"/> is the case that makes
        ///     the distinction necessary -- its OnDestroy waits for an exit animation whose lifetime is
        ///     destroyed moments later, so routing teardown through it would strand every caller waiting
        ///     on <see cref="PopTask"/>.
        /// </remarks>
        protected virtual Task OnTeardown()
        {
            CompletePop();

            return Task.CompletedTask;
        }

        /// <summary>
        ///     Answers everyone waiting on <see cref="PopTask"/>, from whichever ending got here first.
        /// </summary>
        /// <remarks>
        ///     Deferred through the zone rather than completed inline, because both endings can be reached
        ///     from inside disposal, and disposal runs within <c>Atom.NoWatch</c> (see
        ///     <c>BuilderState.Dispose</c>). A continuation resumed there would read atoms without
        ///     registering a dependency and build reactions that never fire, failing silently. The next
        ///     frame is the first moment the call stack has unwound and tracking is live again.
        ///     <para>
        ///         TrySetResult rather than SetResult: the two endings are not mutually exclusive in
        ///         principle, and a route that has already answered must not throw when asked again.
        ///     </para>
        /// </remarks>
        private void CompletePop()
        {
            Zone.Current.NextFrame(() => _popCompleter.TrySetResult(_popResult));
        }

        public bool HandleBack() => _backAction?.Invoke() ?? false;

        public abstract Widget Build(BuildContext context);

        public void SetResult(object result)
        {
            _popResult = result;
        }

        void IBackActionOwner.SetBackAction(Func<bool> action)
        {
            _backAction = action;
        }

        [Obsolete("await route is Obsolete. Use route.PopTask or route.PushTask instead")]
        public TaskAwaiter<object> GetAwaiter()
        {
            return PopTask.GetAwaiter();
        }
    }

    public enum ScreenEvent
    {
        Create = 0,
        Resume = 1,
        Focus = 2,
        Unfocus = 3,
        Pause = 4,
        Destroy = 5,

        /// <summary>
        ///     The route is ending because the tree it lives in is being disposed, not because navigation
        ///     removed it.
        /// </summary>
        Teardown = 6,
    }

    public enum ScreenState
    {
        Initializing = 0,
        Created = 1,
        Resumed = 2,
        Focused = 3,
        Destroyed = 4,
    }

    public enum RouteModalType
    {
        Fullscreen,
        Popup,
    }

    public class RouteSettings
    {
        public string Name { get; }

        public RouteModalType ModalType { get; }

        public RouteSettings(string name, RouteModalType modalType)
        {
            Name = name;
            ModalType = modalType;
        }
    }

    public class RouteBuilder : Route
    {
        private readonly Func<BuildContext, Widget> _pageBuilder;

        public RouteBuilder(RouteSettings settings, Func<BuildContext, Widget> pageBuilder) : base(settings)
        {
            _pageBuilder = pageBuilder;
        }

        public override Widget Build(BuildContext context)
        {
            return _pageBuilder(context);
        }
    }
}