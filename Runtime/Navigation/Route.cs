using System.Runtime.CompilerServices;
using UniMob.UI.Internal;

namespace UniMob.UI.Navigation
{
    using System;
    using System.Threading.Tasks;
    using UnityEngine;

    public abstract class Route : IBackActionOwner
    {
        private readonly RouteSettings _settings;
        private readonly TriggerStateMachine<ScreenState, ScreenEvent, Task> _machine;

        // Continuations run asynchronously so that the typed and untyped result complete as one: see
        // CompletePop.
        private readonly TaskCompletionSource<PopResult> _popCompleter =
            new TaskCompletionSource<PopResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<object?> _pushCompleter =
            new TaskCompletionSource<object?>();
        private readonly TaskCompletionSource<object?> _disposeCompleter =
            new TaskCompletionSource<object?>();

        private readonly MutableAtom<ScreenState> _screenState = Atom.Value(
            ScreenState.Initializing
        );

        private Func<bool>? _backAction;

        // What PopTask completes with. A pop command overwrites it before the route is destroyed; every
        // other ending -- teardown, an un-asked replace, the navigator emptying itself -- leaves it at this
        // default, so a route removed without being asked reports exactly that.
        private PopResult _popResult = PopResult.Teardown();

        protected Route(RouteSettings settings)
        {
            _settings = settings;
            _machine = BuildStateMachine();
        }

        /// <summary>
        ///     Which point of its lifecycle the route has reached. An atom: follow it with a reaction.
        /// </summary>
        /// <remarks>
        ///     Reads "the machine has entered this state", not "the transition has finished": the machine
        ///     moves before it runs the transition's handler, so a <see cref="PageRoute"/> being popped
        ///     reports <see cref="ScreenState.Destroyed"/> for the whole of its exit animation. Both endings
        ///     (removed by navigation, torn down with the tree) land on Destroyed; to tell them apart,
        ///     subscribe to <see cref="ScreenEventApplied"/>.
        /// </remarks>
        public ScreenState ScreenState => _screenState.Value;

        /// <summary>
        ///     Raised for each screen event the state machine accepts, as it is applied: one firing per
        ///     transition, in order, self-transitions included. An event the machine chains onwards fires
        ///     once per step, so destroying a focused route reports Destroy three times.
        /// </summary>
        /// <remarks>
        ///     Raised before the transition's handler runs, so <see cref="ScreenState"/> read from a
        ///     subscriber is the state this event has just reached. Subscribers are told, never consulted:
        ///     one that throws is logged, and neither aborts the transition nor costs later subscribers
        ///     their notification.
        /// </remarks>
        public event Action<ScreenEvent>? ScreenEventApplied;

        public RouteModalType ModalType => _settings.ModalType;

        /// <summary>
        ///     Completes when the route has left the stack, with what it left with. A <see cref="Route{T}"/>
        ///     also offers the same result typed, as <c>Result</c>.
        /// </summary>
        public Task<PopResult> PopTask => _popCompleter.Task;

        public Task PushTask => _pushCompleter.Task;
        public Task DisposeTask => _disposeCompleter.Task;

        public string Key => _settings.Name;

        /// <summary>
        ///     The navigator this route was pushed onto, or null while it has never been pushed. Set when
        ///     the push is issued and kept after the route leaves the stack, so that a late
        ///     <see cref="Pop"/> can answer <see cref="PopOutcome.NotTopmost"/>.
        /// </summary>
        /// <remarks>
        ///     Internal: holding a route lets its holder close it or ask for it to be closed, through
        ///     <see cref="Pop"/> and <see cref="RequestPop"/>, not drive its navigator.
        /// </remarks>
        /// <summary>The navigator this route is on, or null before it is attached to one.</summary>
        internal NavigatorState? Navigator { get; private set; }

        internal void AttachTo(NavigatorState navigator)
        {
            Navigator = navigator;
        }

        /// <summary>
        ///     Closes the route without a value, on its own authority: nobody is asked. For the route's
        ///     owner, meaning its own content or the code that pushed it. Chrome and the system ask instead,
        ///     through <see cref="NavigatorState.RequestPop"/>.
        /// </summary>
        /// <returns>
        ///     <see cref="PopOutcome.Popped"/>, or why nothing happened. <see cref="PopOutcome.NotTopmost"/>
        ///     also for a route that was never pushed.
        /// </returns>
        public Task<PopOutcome> Pop()
        {
            return Navigator == null
                ? Task.FromResult(PopOutcome.NotTopmost)
                : Navigator.PopRoute(this, PopResult.None());
        }

        /// <summary>
        ///     Asks for this route to be popped, as chrome or the system would, so that
        ///     <see cref="OnPopRequested"/> gets its say. For the chrome a route draws for itself: a barrier
        ///     that dismisses on tap, its own close button. Same terms as
        ///     <see cref="NavigatorState.RequestPop"/>.
        /// </summary>
        /// <returns>
        ///     As <see cref="NavigatorState.RequestPop"/>. <see cref="PopOutcome.NotTopmost"/> also for a
        ///     route that was never pushed.
        /// </returns>
        protected Task<PopOutcome> RequestPop(object request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            return Navigator == null
                ? Task.FromResult(PopOutcome.NotTopmost)
                : Navigator.RequestPop(this, request);
        }

        /// <summary>
        ///     Consulted when chrome or the system asks for this route to be popped. Answer
        ///     <see cref="PopDecision.Refuse"/> to keep the route on screen. Runs outside the navigator's
        ///     command loop, so it may take as long as it likes and may itself navigate -- push a dialog and
        ///     await it, say; the pop, if allowed, is queued afterwards and only lands if the route is still
        ///     on top. Untyped routes cannot attach a value to their answer; <see cref="Route{T}"/> can.
        /// </summary>
        protected virtual Task<PopDecision> OnPopRequested(object request)
        {
            return Task.FromResult(PopDecision.Allow());
        }

        /// <summary>
        ///     The navigator's view of the answer, untyped. <see cref="Route{T}"/> overrides this to route
        ///     the question through its typed hook instead.
        /// </summary>
        internal virtual Task<PopDecision> DecideAsync(object request) => OnPopRequested(request);

        internal void SetPopResult(PopResult result)
        {
            _popResult = result;
        }

        /// <summary>
        ///     Told once, with the result PopTask is about to complete with. <see cref="Route{T}"/> completes
        ///     its typed task from here.
        /// </summary>
        protected virtual void OnPopCompleted(PopResult result) { }

        private TriggerStateMachine<ScreenState, ScreenEvent, Task> BuildStateMachine()
        {
            // Initializing → Created → Destroyed
            //                  ↓ ↑
            //                Resumed
            //                  ↓ ↑
            //                Focused
            var fsm = new TriggerStateMachine<ScreenState, ScreenEvent, Task>(
                ScreenState.Initializing
            );

            fsm.Transitioned += PublishTransition;

            fsm.On(ScreenEvent.Create)
                .Allow(ScreenState.Initializing, ScreenState.Created, ExecTransition(OnCreate))
                .Allow(ScreenState.Created, ScreenState.Created);

            fsm.On(ScreenEvent.Resume)
                .Allow(
                    ScreenState.Created,
                    ScreenState.Resumed,
                    ExecTransition(OnResume, ScreenEvent.Resume)
                )
                .Allow(ScreenState.Resumed, ScreenState.Resumed);

            fsm.On(ScreenEvent.Focus)
                .Allow(
                    ScreenState.Created,
                    ScreenState.Resumed,
                    ExecTransition(OnResume, ScreenEvent.Focus)
                )
                .Allow(ScreenState.Resumed, ScreenState.Focused, ExecTransition(OnFocus))
                .Allow(ScreenState.Focused, ScreenState.Focused);

            fsm.On(ScreenEvent.Unfocus)
                .Allow(ScreenState.Focused, ScreenState.Resumed, ExecTransition(OnFocusLost))
                .Allow(ScreenState.Resumed, ScreenState.Resumed);

            fsm.On(ScreenEvent.Pause)
                .Allow(
                    ScreenState.Focused,
                    ScreenState.Resumed,
                    ExecTransition(OnFocusLost, ScreenEvent.Pause)
                )
                .Allow(ScreenState.Resumed, ScreenState.Created, ExecTransition(OnPause))
                .Allow(ScreenState.Created, ScreenState.Created);

            fsm.On(ScreenEvent.Destroy)
                .Allow(
                    ScreenState.Focused,
                    ScreenState.Resumed,
                    ExecTransition(OnFocusLost, ScreenEvent.Destroy)
                )
                .Allow(
                    ScreenState.Resumed,
                    ScreenState.Created,
                    ExecTransition(OnPause, ScreenEvent.Destroy)
                )
                .Allow(ScreenState.Created, ScreenState.Destroyed, ExecTransition(OnDestroy))
                .Allow(ScreenState.Destroyed, ScreenState.Destroyed);

            // Teardown: the tree is going away, as opposed to Destroy, where navigation removed the route.
            // Every live state goes straight to Destroyed, skipping OnFocusLost and OnPause: there is
            // nothing underneath to hand the screen back to, and skipping the chain is what keeps teardown
            // synchronous, since the chain is what starts a PageRoute's exit animation.
            //
            // Destroyed is included with the handler, not as a bare self-transition: a route caught
            // mid-destroy is already Destroyed while it waits out its exit animation, that wait is
            // abandoned with the tree, and without the handler its callers would wait forever.
            fsm.On(ScreenEvent.Teardown)
                .Allow(ScreenState.Initializing, ScreenState.Destroyed, ExecTransition(OnTeardown))
                .Allow(ScreenState.Created, ScreenState.Destroyed, ExecTransition(OnTeardown))
                .Allow(ScreenState.Resumed, ScreenState.Destroyed, ExecTransition(OnTeardown))
                .Allow(ScreenState.Focused, ScreenState.Destroyed, ExecTransition(OnTeardown))
                .Allow(ScreenState.Destroyed, ScreenState.Destroyed, ExecTransition(OnTeardown));

            return fsm;
        }

        /// <summary>
        ///     Mirrors an accepted transition onto the route's two public channels, intermediate states of
        ///     a chained event included.
        /// </summary>
        private void PublishTransition(ScreenState state, ScreenEvent screenEvent)
        {
            _screenState.Value = state;

            var subscribers = ScreenEventApplied;

            if (subscribers == null)
            {
                return;
            }

            // One subscriber at a time, each contained: a throw neither aborts the transition nor costs
            // later subscribers their notification, and walking a copy of the invocation list makes
            // unsubscribing from inside a callback harmless.
            //
            // Untracked: transitions run on the caller's stack, and a subscriber reading ScreenState would
            // otherwise graft that dependency onto whatever computation is running.
            using (Atom.NoWatch)
            {
                foreach (var subscriber in subscribers.GetInvocationList())
                {
                    try
                    {
                        ((Action<ScreenEvent>)subscriber).Invoke(screenEvent);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }

        private Func<Task, Task> ExecTransition(
            Func<Task> handler,
            ScreenEvent? screenEvent = null
        ) => previous => ExecuteTransitionInternal(previous, handler, screenEvent);

        /// <remarks>
        ///     Every task is awaited unconditionally, even one that is already complete. <c>IsCompleted</c>
        ///     is true for a faulted task too, and awaiting is the only thing that rethrows: behind a guard,
        ///     a failed handler's exception stays sealed in its task, the navigator carries on removing the
        ///     route, and <see cref="PopTask"/> never completes. Awaiting a completed task does not yield,
        ///     so the guard would buy nothing.
        /// </remarks>
        private async Task ExecuteTransitionInternal(
            Task previous,
            Func<Task> handler,
            ScreenEvent? screenEvent
        )
        {
            if (previous != null)
            {
                await previous;
            }

            var current = handler();

            if (current != null)
            {
                await current;
            }

            if (screenEvent.HasValue)
            {
                var next = _machine.Trigger(screenEvent.Value);

                if (next != null)
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

            // Read off the machine, not the atom: a diagnostic must not add a dependency to a running computation.
            Debug.LogErrorFormat(
                "Cannot {0} scene {1} in {2} state",
                screenEvent,
                GetType().Name,
                _machine.State
            );
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
        ///     Ends the route because the tree it lives in is going away, not because navigation removed
        ///     it. Separate from <see cref="OnDestroy"/>, which subclasses override to finish a transition;
        ///     there is none to finish here, and a <see cref="PageRoute"/> waiting for its exit animation
        ///     would strand <see cref="PopTask"/>.
        /// </summary>
        protected virtual Task OnTeardown()
        {
            CompletePop();

            return Task.CompletedTask;
        }

        /// <summary>
        ///     Answers everyone waiting on <see cref="PopTask"/>, from whichever ending got here first.
        /// </summary>
        /// <remarks>
        ///     Deferred to the next frame: both endings can be reached from inside disposal, which runs
        ///     within <c>Atom.NoWatch</c>, and a continuation resumed there would read atoms without
        ///     registering dependencies. Both the untyped and the typed completer run their continuations
        ///     asynchronously, so an awaiter of either never resumes between the two being set.
        /// </remarks>
        private void CompletePop()
        {
            var result = _popResult;

            Zone.Current.NextFrame(() =>
            {
                // TrySetResult: the two endings are not mutually exclusive, and answering twice must not throw.
                if (_popCompleter.TrySetResult(result))
                {
                    OnPopCompleted(result);
                }
            });
        }

        public bool HandleBack() => _backAction?.Invoke() ?? false;

        public abstract Widget Build(BuildContext context);

        void IBackActionOwner.SetBackAction(Func<bool> action)
        {
            _backAction = action;
        }

        [Obsolete("await route is Obsolete. Use route.PopTask or route.PushTask instead")]
        public TaskAwaiter<PopResult> GetAwaiter()
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

        public RouteBuilder(RouteSettings settings, Func<BuildContext, Widget> pageBuilder)
            : base(settings)
        {
            _pageBuilder = pageBuilder;
        }

        public override Widget Build(BuildContext context)
        {
            return _pageBuilder(context);
        }
    }
}
