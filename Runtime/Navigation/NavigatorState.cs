namespace UniMob.UI.Navigation
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using UniMob.UI.Internal.Views;
    using UniMob.UI.Widgets;
    using UnityEngine;

    public class NavigatorState : ViewState<Navigator>, INavigatorState, IMultiChildLayoutState
    {
        private readonly StateCollectionHolder _states;
        private readonly NavigatorStack _stack;

        private readonly Queue<NavigatorCommand[]> _pendingCommands =
            new Queue<NavigatorCommand[]>();
        private readonly Stack<Route> _pendingPause = new Stack<Route>();

        // One request per route at a time. A second requester arriving while the route is still deciding
        // shares the pending answer instead of asking again, so a route is never asked twice about the
        // same moment and two requesters never race each other to the same pop.
        private readonly Dictionary<Route, Task<PopOutcome>> _pendingRequests =
            new Dictionary<Route, Task<PopOutcome>>();

        private Task _task = Task.CompletedTask;
        private bool _processing;

        public override WidgetViewReference View { get; } =
            WidgetViewReference.Registered("UniMob.NavigatorView");

        public NavigatorState()
        {
            _stack = new NavigatorStack();
            _states = CreateChildren(_ => _stack.Widgets);
        }

        public bool AutoFocus { get; set; } = true;

        [Atom]
        public IState[] Screens => _states.Value;

        IState[] IMultiChildLayoutState.Children => Screens;

        /// <summary>
        ///     The routes on the stack, topmost first. A fresh snapshot after every push, pop and replace,
        ///     so a reaction over it fires per mutation; the live collection would compare equal to itself
        ///     and never obsolete a subscriber.
        /// </summary>
        [Atom]
        public IReadOnlyCollection<Route> NavigationStack => _stack.Routes;

        [Atom]
        public Route TopmostRoute => _stack.TopmostRoute;

        /// <summary>
        ///     How deep the stack is and what is on top of it: <c>"3 routes: room-edit"</c>. Read off the
        ///     stack directly, since <see cref="TopmostRoute"/> throws on the empty stack this state has
        ///     before <c>InitState</c>.
        /// </summary>
        public override string GetDiagnosticInfo()
        {
            var depth = _stack.Count;
            if (depth == 0)
            {
                return "empty";
            }

            var top = _stack.TopmostRoute?.Key;
            var routes = depth == 1 ? "1 route" : depth + " routes";

            return string.IsNullOrEmpty(top) ? routes : routes + ": " + top;
        }

        public override void InitState()
        {
            base.InitState();

            PushNamed(Widget.InitialRoute);
        }

        /// <summary>
        ///     Ends every route still on the stack before the state tree takes them apart. Otherwise the
        ///     routes are disposed without being destroyed, nothing completes their <c>PopTask</c>, and
        ///     every awaiter waits forever. Synchronous, because Dispose cannot await: that is why teardown
        ///     is its own event and not a reuse of Destroy, which can block on an exit animation.
        /// </summary>
        public override void Dispose()
        {
            TearDownRoutes();

            base.Dispose();
        }

        private void TearDownRoutes()
        {
            foreach (var route in _stack)
            {
                // Completes synchronously; the returned task is already finished. Nothing on this path
                // mutates the stack, so enumerating it while triggering is safe.
                route.ApplyScreenEvent(ScreenEvent.Teardown);
            }
        }

        private Route CreateRoute(string name)
        {
            if (!Widget.Routes.TryGetValue(name, out var routeBuilder))
            {
                throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown route");
            }

            var route = routeBuilder();

            if (route == null)
            {
                throw new ArgumentException("Route builder result null");
            }

            return route;
        }

        public Route PushNamed(string routeName)
        {
            var route = CreateRoute(routeName);
            return Push(route);
        }

        public Route Push(Route route)
        {
            if (route == null)
                throw new ArgumentNullException(nameof(route));

            // Attached when the push is issued, not when the command runs: an owner that pops its route
            // while the command waits behind an exit animation must find a navigator to queue the pop with.
            route.AttachTo(this);
            ApplyCommands(new NavigatorCommand.Push(route));
            return route;
        }

        public Route NewRootNamed(string routeName)
        {
            var route = CreateRoute(routeName);
            return NewRoot(route);
        }

        /// <summary>
        ///     Empties the stack and puts <paramref name="route"/> in its place, without asking any of the
        ///     routes it removes. Everything removed completes with <see cref="PopCause.Teardown"/>.
        /// </summary>
        public Route NewRoot(Route route)
        {
            if (route == null)
                throw new ArgumentNullException(nameof(route));

            route.AttachTo(this);
            ApplyCommands(
                new NavigatorCommand.PopTo(null),
                new NavigatorCommand.Replace(route, null, PopResult.Teardown())
            );
            return route;
        }

        public Route ReplaceNamed(string routeName)
        {
            var route = CreateRoute(routeName);
            return Replace(route);
        }

        /// <summary>
        ///     Swaps the topmost route for <paramref name="route"/> without asking it. Teardown-class, like
        ///     <see cref="NewRoot"/>: the removed route completes with <see cref="PopCause.Teardown"/>.
        ///     To swap a route that gets a say, use <see cref="RequestReplace"/>.
        /// </summary>
        public Route Replace(Route route)
        {
            if (route == null)
                throw new ArgumentNullException(nameof(route));

            route.AttachTo(this);
            ApplyCommands(new NavigatorCommand.Replace(route, null, PopResult.Teardown()));
            return route;
        }

        /// <summary>
        ///     Asks <paramref name="route"/> whether it may be popped and, if it agrees, pops it carrying
        ///     <paramref name="request"/> and whatever value the route attached to its answer.
        /// </summary>
        /// <remarks>
        ///     The route is asked outside the command loop: its hook may await and may navigate. The pop is
        ///     queued afterwards and lands only if the route is still topmost, else
        ///     <see cref="PopOutcome.NotTopmost"/>. A route already being asked is not asked again; a
        ///     second requester shares the pending outcome, even while the route is covered by a dialog it
        ///     pushed in order to decide. <paramref name="request"/> is carried through untouched to the
        ///     hook and to <see cref="PopResult.Request"/>; it may not be null, since a null request marks
        ///     a self-close.
        /// </remarks>
        public Task<PopOutcome> RequestPop(Route route, object request)
        {
            if (route == null)
                throw new ArgumentNullException(nameof(route));
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            // Before the topmost check: a route deciding behind its own dialog is not on top, and a second
            // requester must still join it. A pending entry only exists for a route that was ours and on
            // top when asked.
            if (_pendingRequests.TryGetValue(route, out var pending))
            {
                return pending;
            }

            if (route.Navigator != this || Topmost() != route)
            {
                return Task.FromResult(PopOutcome.NotTopmost);
            }

            return StartRequest(
                route,
                request,
                decision => PopRoute(route, decision.ToResult(request))
            );
        }

        /// <summary>
        ///     Asks <paramref name="outgoing"/> whether it may go and, if it agrees, replaces it with
        ///     <paramref name="incoming"/> in one stack change, so nothing ever observes the stack without
        ///     either of them. Same terms as <see cref="RequestPop"/>; the outgoing route's result carries
        ///     <paramref name="request"/> and the value its answer supplied.
        /// </summary>
        /// <returns>
        ///     <see cref="PopOutcome.Popped"/> when the swap happened. On any other outcome
        ///     <paramref name="incoming"/> was not pushed.
        /// </returns>
        public Task<PopOutcome> RequestReplace(Route outgoing, Route incoming, object request)
        {
            if (outgoing == null)
                throw new ArgumentNullException(nameof(outgoing));
            if (incoming == null)
                throw new ArgumentNullException(nameof(incoming));
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            // Before the topmost check, as in RequestPop.
            if (_pendingRequests.TryGetValue(outgoing, out var pending))
            {
                // Someone else is already closing it, so this replace does not happen either way: Refused
                // stays Refused, and their Popped becomes NotTopmost here, since incoming was never pushed.
                return MapJoined(pending);
            }

            if (outgoing.Navigator != this || Topmost() != outgoing)
            {
                return Task.FromResult(PopOutcome.NotTopmost);
            }

            return StartRequest(
                outgoing,
                request,
                decision => ReplaceRoute(outgoing, incoming, decision.ToResult(request))
            );
        }

        /// <summary>
        ///     Pops route after route, asking each, until <paramref name="target"/> is on top -- or, when
        ///     <paramref name="target"/> is null, until one route is left. Stops at the first route that
        ///     refuses or that is no longer where the walk expected it, and says which.
        /// </summary>
        /// <remarks>
        ///     One request per route, in turn, so that each is asked while it is genuinely topmost. Between
        ///     two steps the revealed route is resumed and focused as after any pop.
        /// </remarks>
        public Task<PopToOutcome> RequestPopTo(Route target, object request)
        {
            // Outside the async body, so a bad argument throws at the call rather than faulting the task.
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            return RequestPopToAsync(target, request);
        }

        private async Task<PopToOutcome> RequestPopToAsync(Route target, object request)
        {
            while (true)
            {
                var top = Topmost();

                if (top == null || top == target || target == null && _stack.Count <= 1)
                {
                    return PopToOutcome.ReachedTarget();
                }

                if (_stack.Count <= 1)
                {
                    return PopToOutcome.Stopped(top, PopOutcome.LastRoute);
                }

                var outcome = await RequestPop(top, request);

                if (outcome != PopOutcome.Popped)
                {
                    return PopToOutcome.Stopped(top, outcome);
                }
            }
        }

        /// <summary>
        ///     The pop a route performs on its own authority; reached through <see cref="Route.Pop"/>.
        /// </summary>
        internal Task<PopOutcome> PopRoute(Route route, PopResult result)
        {
            var command = new NavigatorCommand.Pop(route, result);
            ApplyCommands(command);
            return command.Outcome.Task;
        }

        private Task<PopOutcome> ReplaceRoute(
            Route outgoing,
            Route incoming,
            PopResult outgoingResult
        )
        {
            var command = new NavigatorCommand.Replace(incoming, outgoing, outgoingResult);
            ApplyCommands(command);
            return command.Outcome.Task;
        }

        private async Task<PopOutcome> RunRequest(
            Route route,
            object request,
            Func<PopDecision, Task<PopOutcome>> commit
        )
        {
            try
            {
                var decision = await route.DecideAsync(request);

                if (!decision.IsAllowed)
                {
                    return PopOutcome.Refused;
                }

                return await commit(decision);
            }
            finally
            {
                _pendingRequests.Remove(route);
            }
        }

        /// <summary>
        ///     Takes the route's slot, then asks. The slot is what every later requester joins.
        /// </summary>
        private Task<PopOutcome> StartRequest(
            Route route,
            object request,
            Func<PopDecision, Task<PopOutcome>> commit
        )
        {
            // The slot is a placeholder registered before any route code runs. The hook runs synchronously
            // up to its first await and may navigate from there; a second request for the same route
            // arriving in that window (from the hook, or from an observer of a push it makes) must find
            // the question in progress, not ask again and queue a competing close.
            var pending = new TaskCompletionSource<PopOutcome>();
            _pendingRequests[route] = pending.Task;
            Answer(pending, RunRequest(route, request, commit));
            return pending.Task;
        }

        /// <summary>
        ///     Completes the slot from the request that ran, with whatever it ended in: an outcome, a
        ///     failure, or cancellation.
        /// </summary>
        private static void Answer(TaskCompletionSource<PopOutcome> pending, Task<PopOutcome> run)
        {
            // Synchronously: RunRequest frees the slot in its finally, so a requester that resumes and asks
            // again must start a fresh question, not join this finished one. Inner exceptions, not the
            // aggregate, so awaiting the slot throws what RunRequest threw.
            run.ContinueWith(
                finished =>
                {
                    if (finished.IsCanceled)
                    {
                        pending.TrySetCanceled();
                    }
                    else if (finished.IsFaulted)
                    {
                        pending.TrySetException(finished.Exception.InnerExceptions);
                    }
                    else
                    {
                        pending.TrySetResult(finished.Result);
                    }
                },
                TaskContinuationOptions.ExecuteSynchronously
            );
        }

        private static async Task<PopOutcome> MapJoined(Task<PopOutcome> pending)
        {
            var outcome = await pending;
            return outcome == PopOutcome.Popped ? PopOutcome.NotTopmost : outcome;
        }

        /// <summary>
        ///     The route on top, untracked (control flow, not observation), or null on an empty stack
        ///     where <see cref="TopmostRoute"/> throws.
        /// </summary>
        private Route Topmost()
        {
            return _stack.Count > 0 ? _stack.Peek() : null;
        }

        public bool HandleBack()
        {
            return _stack.Count > 1 && _stack.Peek().HandleBack();
        }

        public async Task ApplyScreenEvent(ScreenEvent evt)
        {
            if (_stack.Count == 0)
            {
                return;
            }

            if (!_task.IsCompleted)
            {
                await _task;
            }

            switch (evt)
            {
                case ScreenEvent.Create:
                    break;

                case ScreenEvent.Resume:
                    await UnpauseScreens();
                    break;

                case ScreenEvent.Focus:
                    await _stack.Peek().ApplyScreenEvent(ScreenEvent.Focus);
                    break;

                case ScreenEvent.Unfocus:
                    await _stack.Peek().ApplyScreenEvent(ScreenEvent.Unfocus);
                    break;

                case ScreenEvent.Pause:
                    await PauseScreens();
                    break;

                case ScreenEvent.Destroy:
                    while (_stack.Count > 0)
                    {
                        await _stack.Peek().ApplyScreenEvent(ScreenEvent.Destroy);
                        _stack.Pop();
                    }

                    break;

                case ScreenEvent.Teardown:
                    TearDownRoutes();
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(evt), evt, "Unexpected event");
            }
        }

        /// <summary>
        ///     Queues a batch of commands, and starts the loop that drains the queue if one is not already
        ///     running.
        /// </summary>
        private void ApplyCommands(params NavigatorCommand[] commands)
        {
            _pendingCommands.Enqueue(commands);

            // A flag, not _task.IsCompleted: _task is only assigned once the loop reaches its first real
            // await, and a navigation whose handlers all complete synchronously never gets there. Anything
            // navigating from inside that window (an observer callback, OnInitialize) would start a second
            // loop over a stack the first has only half moved.
            if (_processing)
            {
                return;
            }

            _task = ProcessCommandsLoop();
        }

        private async Task ProcessCommandsLoop()
        {
            // Before the first await, so the synchronous prefix of the loop is covered too.
            _processing = true;

            try
            {
                while (_pendingCommands.Count > 0)
                {
                    await ProcessCommands(_pendingCommands.Dequeue());
                }
            }
            catch (OperationCanceledException)
            {
                // A transition abandoned, not failed: an exit animation cancelled because the tree disposed
                // its lifetime, which is every unmount that lands mid-transition. Not worth a red log. Like
                // the general catch, it ends the loop and abandons the remaining queued commands.
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                _processing = false;
            }
        }

        private async Task ProcessCommands(NavigatorCommand[] commands)
        {
            if (commands == null)
                throw new ArgumentNullException(nameof(commands));

            foreach (var command in commands)
            {
                await ProcessCommand(command);
            }

            if (AutoFocus && _stack.Count > 0)
            {
                await _stack.Peek().ApplyScreenEvent(ScreenEvent.Focus);
            }
        }

        private async Task ProcessCommand(NavigatorCommand command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            try
            {
                switch (command)
                {
                    case NavigatorCommand.Pop pop:
                        await PopInternal(pop);
                        return;

                    case NavigatorCommand.PopTo backTo:
                        await PopToInternal(backTo);
                        return;

                    case NavigatorCommand.Push forward:
                        await PushInternal(forward);
                        return;

                    case NavigatorCommand.Replace replace:
                        await ReplaceInternal(replace);
                        return;
                }

                Debug.LogWarning($"Unexpected navigator command: {command.GetType().Name}");
            }
            catch (Exception e)
            {
                // The loop logs and stops, but it cannot answer whoever issued this command. Left pending,
                // the outcome would also hold the route's slot in _pendingRequests forever, and every later
                // request for that route would join a task that never completes.
                command.Fail(e);
                throw;
            }
        }

        private async Task PushInternal(NavigatorCommand.Push push)
        {
            var screen = push.Route;
            var observers = SnapshotObservers();
            var covered = _stack.Count > 0 ? _stack.Peek() : null;

            // Attached first, so even OnInitialize can reach the navigator.
            screen.AttachTo(this);

            // Announced before the route is built: initialization is the one step that can take
            // arbitrarily long (a preloading route), so a bracket drawn after it would span nothing worth
            // announcing. The cost is that a push whose initialization fails leaves WillPush unmatched.
            NotifyWillPush(observers, screen, covered);

            // Built before anything on screen is disturbed: OnInitialize is the one step here that can
            // fail or take long, and a route that cannot be built must leave the navigator exactly as it
            // was. It also keeps the visible screen unpaused while the next one loads.
            await InitializeScreen(screen);

            if (screen.ModalType == RouteModalType.Fullscreen)
            {
                await PauseScreens();
            }
            else if (_stack.Count >= 1 && IsTopmostFocused())
            {
                await _stack.Peek().ApplyScreenEvent(ScreenEvent.Unfocus);
            }

            _stack.Push(screen);

            // After the mutation: an observer may read NavigationStack and TopmostRoute from its callback,
            // and they must agree with what it is being told.
            NotifyDidPush(observers, screen, covered);

            await screen.ApplyScreenEvent(ScreenEvent.Create);
        }

        /// <summary>
        ///     Whether the topmost route currently holds focus. Untracked: control flow, not observation,
        ///     so a push from inside a computation does not make it depend on a route's lifecycle.
        /// </summary>
        private bool IsTopmostFocused()
        {
            using (Atom.NoWatch)
            {
                return _stack.Peek().ScreenState == ScreenState.Focused;
            }
        }

        private async Task ReplaceInternal(NavigatorCommand.Replace replace)
        {
            var screen = replace.Route;
            var observers = SnapshotObservers();

            // A replace on an empty navigator is announced as a push.
            var replaced = _stack.Count > 0 ? _stack.Peek() : null;

            // A requested replace names the route it was agreed with; if that route is no longer on top
            // (it popped itself, or was covered while deciding), the swap it agreed to no longer exists.
            if (replace.Target != null && replaced != replace.Target)
            {
                replace.Outcome.TrySetResult(PopOutcome.NotTopmost);
                return;
            }

            screen.AttachTo(this);

            // Before the route is built, as in PushInternal, and unmatched on the same terms.
            if (replaced != null)
            {
                NotifyWillReplace(observers, screen, replaced);
            }
            else
            {
                NotifyWillPush(observers, screen, null);
            }

            // Built before the outgoing route is touched: a route that cannot be built must not leave the
            // navigator one route short with nothing in its place.
            await InitializeScreen(screen);

            if (_stack.Count > 0)
            {
                // Set before the destroy so OnDestroy sees it.
                _stack.Peek().SetPopResult(replace.OutgoingResult);

                // Unlike PopInternal, the removal is not committed against a failing destroy: a replace
                // removes before it adds, so committing would abort with an empty navigator.
                await _stack.Peek().ApplyScreenEvent(ScreenEvent.Destroy);
                _stack.Pop();

                if (_stack.Count > 0)
                {
                    var isNewFullscreen = replace.Route.ModalType == RouteModalType.Fullscreen;
                    var isOldFullscreen = _stack.Peek().ModalType == RouteModalType.Fullscreen;

                    if (isNewFullscreen && !isOldFullscreen)
                    {
                        await PauseScreens();
                    }
                    else if (!isNewFullscreen && isOldFullscreen)
                    {
                        await UnpauseScreens();
                    }
                }
            }

            _stack.Push(screen);

            if (replaced != null)
            {
                NotifyDidReplace(observers, screen, replaced);
            }
            else
            {
                NotifyDidPush(observers, screen, null);
            }

            // Answered once the swap is on the stack, before the incoming route is created: the question
            // was whether the outgoing route went.
            replace.Outcome.TrySetResult(PopOutcome.Popped);

            await screen.ApplyScreenEvent(ScreenEvent.Create);
        }

        private async Task PopToInternal(NavigatorCommand.PopTo popTo)
        {
            // One snapshot for the whole command: every route it removes is announced to the same observers.
            var observers = SnapshotObservers();

            while (_stack.Count > 1)
            {
                if (popTo.Route != null && _stack.Peek().Key == popTo.Route.Key)
                {
                    break;
                }

                var first = _stack.Peek();
                var revealed = _stack.PeekBelow();

                NotifyWillPop(observers, first, revealed);

                // Un-asked: NewRoot clearing the way, reported as teardown.
                first.SetPopResult(PopResult.Teardown());

                var destroyTask = first.ApplyScreenEvent(ScreenEvent.Destroy);

                if (first.ModalType == RouteModalType.Fullscreen)
                {
                    await UnpauseScreens(skip: 1);
                }

                // Removal commits per iteration, as in PopInternal: a throw part-way through keeps every
                // route already removed removed, and stops at the one that failed.
                try
                {
                    await destroyTask;
                }
                finally
                {
                    _stack.Pop();
                    NotifyDidPop(observers, first, revealed);
                }
            }
        }

        private async Task PopInternal(NavigatorCommand.Pop pop)
        {
            if (_stack.Count <= 1)
            {
                pop.Outcome.TrySetResult(
                    _stack.Count == 1 && _stack.Peek() == pop.Target
                        ? PopOutcome.LastRoute
                        : PopOutcome.NotTopmost
                );
                return;
            }

            var first = _stack.Peek();

            // Pops name their target: one issued for a route that has since left, or been covered, does
            // nothing rather than removing whatever is on top now.
            if (first != pop.Target)
            {
                pop.Outcome.TrySetResult(PopOutcome.NotTopmost);
                return;
            }

            var observers = SnapshotObservers();
            var revealed = _stack.PeekBelow();

            NotifyWillPop(observers, first, revealed);

            first.SetPopResult(pop.Result);

            var destroyTask = first.ApplyScreenEvent(ScreenEvent.Destroy);

            if (first.ModalType == RouteModalType.Fullscreen)
            {
                await UnpauseScreens(skip: 1);
            }

            // The removal commits even if the transition throws. The state machine moves before it runs the
            // handler, so the route already reports Destroyed while still on the stack and still what
            // TopmostRoute and HandleBack answer with; skipping the pop would leave the two permanently
            // disagreeing. Removals commit, additions do not: nothing has committed before a push.
            try
            {
                await destroyTask;
            }
            finally
            {
                _stack.Pop();

                // Both in the finally: the route is off the stack whatever its transition did, so every
                // WillPop is matched by a DidPop and the outcome reports Popped.
                NotifyDidPop(observers, first, revealed);
                pop.Outcome.TrySetResult(PopOutcome.Popped);
            }
        }

        private async Task PauseScreens()
        {
            foreach (var screen in _stack)
            {
                _pendingPause.Push(screen);

                if (screen.ModalType == RouteModalType.Fullscreen)
                {
                    break;
                }
            }

            while (_pendingPause.Count > 0)
            {
                var screen = _pendingPause.Pop();
                await screen.ApplyScreenEvent(ScreenEvent.Pause);
            }
        }

        private async Task UnpauseScreens(int skip = 0)
        {
            foreach (var screen in _stack)
            {
                if (skip-- != 0)
                {
                    continue;
                }

                await screen.ApplyScreenEvent(ScreenEvent.Resume);

                if (screen.ModalType == RouteModalType.Fullscreen)
                {
                    return;
                }
            }
        }

        private Task InitializeScreen(Route screen)
        {
            return screen.Initialize();
        }

        /// <summary>
        ///     The observers an operation announces to, fixed for the whole of it: the widget can be
        ///     replaced across the operation's awaits, and an observer must hear both edges or neither.
        ///     Untracked, so that a navigation started from inside a computation does not make it depend
        ///     on the navigator's widget.
        /// </summary>
        private INavigatorObserver[] SnapshotObservers()
        {
            IReadOnlyList<INavigatorObserver> observers;

            using (Atom.NoWatch)
            {
                observers = Widget.Observers;
            }

            if (observers == null || observers.Count == 0)
            {
                return Array.Empty<INavigatorObserver>();
            }

            var snapshot = new INavigatorObserver[observers.Count];

            for (var index = 0; index < observers.Count; index++)
            {
                snapshot[index] = observers[index];
            }

            return snapshot;
        }

        // Every callback is dispatched the same way: over the snapshot the operation began with, each
        // observer contained on its own (one that throws is logged, the rest still hear it), and the whole
        // walk untracked, so that an observer reading NavigationStack or a route's ScreenState cannot graft
        // that dependency onto whatever computation the navigation was started from.

        private void NotifyWillPush(
            INavigatorObserver[] observers,
            Route route,
            Route previousRoute
        )
        {
            using (Atom.NoWatch)
            {
                foreach (var observer in observers)
                {
                    try
                    {
                        observer.WillPush(route, previousRoute);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }

        private void NotifyDidPush(INavigatorObserver[] observers, Route route, Route previousRoute)
        {
            using (Atom.NoWatch)
            {
                foreach (var observer in observers)
                {
                    try
                    {
                        observer.DidPush(route, previousRoute);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }

        private void NotifyWillPop(INavigatorObserver[] observers, Route route, Route previousRoute)
        {
            using (Atom.NoWatch)
            {
                foreach (var observer in observers)
                {
                    try
                    {
                        observer.WillPop(route, previousRoute);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }

        private void NotifyDidPop(INavigatorObserver[] observers, Route route, Route previousRoute)
        {
            using (Atom.NoWatch)
            {
                foreach (var observer in observers)
                {
                    try
                    {
                        observer.DidPop(route, previousRoute);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }

        private void NotifyWillReplace(
            INavigatorObserver[] observers,
            Route newRoute,
            Route oldRoute
        )
        {
            using (Atom.NoWatch)
            {
                foreach (var observer in observers)
                {
                    try
                    {
                        observer.WillReplace(newRoute, oldRoute);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }

        private void NotifyDidReplace(
            INavigatorObserver[] observers,
            Route newRoute,
            Route oldRoute
        )
        {
            using (Atom.NoWatch)
            {
                foreach (var observer in observers)
                {
                    try
                    {
                        observer.DidReplace(newRoute, oldRoute);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }
    }

    internal abstract class NavigatorCommand
    {
        /// <summary>
        ///     Told that processing this command threw. A command that answers a requester fails that
        ///     answer here; commands nobody awaits have nothing to do.
        /// </summary>
        public virtual void Fail(Exception exception) { }

        /// <summary>
        ///     Fails an outcome the way an async method would: cancellation cancels it, anything else
        ///     faults it. No-op for an outcome already answered, as a pop whose removal committed before
        ///     its transition failed has.
        /// </summary>
        protected static void FailOutcome(
            TaskCompletionSource<PopOutcome> outcome,
            Exception exception
        )
        {
            if (exception is OperationCanceledException)
            {
                outcome.TrySetCanceled();
            }
            else
            {
                outcome.TrySetException(exception);
            }
        }

        /// <summary>
        ///     Removes <see cref="Target"/> if it is on top when the command runs, and says what happened.
        /// </summary>
        public sealed class Pop : NavigatorCommand
        {
            public Route Target { get; }
            public PopResult Result { get; }
            public TaskCompletionSource<PopOutcome> Outcome { get; } =
                new TaskCompletionSource<PopOutcome>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

            public Pop(Route target, PopResult result)
            {
                Target = target;
                Result = result;
            }

            public override void Fail(Exception exception) => FailOutcome(Outcome, exception);
        }

        /// <summary>
        ///     Un-asked removal down to a route, or to the last one when <see cref="Route"/> is null. Only
        ///     <c>NewRoot</c> issues it.
        /// </summary>
        public sealed class PopTo : NavigatorCommand
        {
            public Route Route { get; }

            public PopTo(Route? route) => Route = route;
        }

        public class Push : NavigatorCommand
        {
            public Route Route { get; }

            public Push(Route route) => Route = route;
        }

        /// <summary>
        ///     Swaps the topmost route for <see cref="Route"/>. With a <see cref="Target"/>, only if that
        ///     is what is on top; without one, whatever is on top, un-asked. <see cref="OutgoingResult"/> is
        ///     what the removed route reports.
        /// </summary>
        public class Replace : NavigatorCommand
        {
            public Route Route { get; }

            public Route? Target { get; }
            public PopResult OutgoingResult { get; }
            public TaskCompletionSource<PopOutcome> Outcome { get; } =
                new TaskCompletionSource<PopOutcome>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

            public Replace(Route route, Route? target, PopResult outgoingResult)
            {
                Route = route;
                Target = target;
                OutgoingResult = outgoingResult;
            }

            public override void Fail(Exception exception) => FailOutcome(Outcome, exception);
        }
    }

    internal class NavigatorStack : IEnumerable<Route>
    {
        private readonly Stack<Route> _stack = new Stack<Route>();
        private readonly List<Widget> _widgets = new List<Widget>();
        private readonly MutableAtom<int> _version = Atom.Value(int.MinValue);

        // Counted here, not as _version.Value++: that is a read as well as a write, and a stack mutated
        // from inside a computation would subscribe that computation and immediately obsolete it.
        private int _revision = int.MinValue;

        private Route[] _snapshot;
        private int _snapshotRevision;

        public int Count => _stack.Count;

        public Route TopmostRoute
        {
            get
            {
                _version.Get();
                return _stack.Peek();
            }
        }

        public List<Widget> Widgets
        {
            get
            {
                _version.Get();
                return _widgets;
            }
        }

        /// <summary>
        ///     A snapshot of the stack, topmost first, rebuilt once per mutation and shared until the next.
        /// </summary>
        public IReadOnlyCollection<Route> Routes
        {
            get
            {
                _version.Get();

                if (_snapshot == null || _snapshotRevision != _revision)
                {
                    _snapshot = _stack.ToArray();
                    _snapshotRevision = _revision;
                }

                return _snapshot;
            }
        }

        public Route Peek() => _stack.Peek();

        /// <summary>
        ///     The route that would become topmost if the current one were removed, or null when there is
        ///     nothing underneath. Untracked, like <see cref="Peek"/> and <see cref="Count"/>; allocation-free.
        /// </summary>
        public Route PeekBelow()
        {
            var topmostSkipped = false;

            foreach (var route in _stack)
            {
                if (topmostSkipped)
                {
                    return route;
                }

                topmostSkipped = true;
            }

            return null;
        }

        public Route Pop()
        {
            _widgets.RemoveAt(_widgets.Count - 1);
            var result = _stack.Pop();
            _version.Value = ++_revision;
            return result;
        }

        public void Push(Route screen)
        {
            _widgets.Add(
                new Builder(screen.Build) { Key = Key.Of(screen), OnDispose = screen.Dispose }
            );
            _stack.Push(screen);
            _version.Value = ++_revision;
        }

        public IEnumerator<Route> GetEnumerator() => _stack.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
