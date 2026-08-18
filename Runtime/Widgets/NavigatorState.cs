namespace UniMob.UI.Widgets
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using JetBrains.Annotations;
    using UniMob.UI.Layout.Internal.Views;
    using UnityEngine;

    public class NavigatorState : ViewState<Navigator>, INavigatorState, IMultiChildLayoutState
    {
        private readonly StateCollectionHolder _states;
        private readonly NavigatorStack _stack;

        private readonly Queue<NavigatorCommand[]> _pendingCommands = new Queue<NavigatorCommand[]>();
        private readonly Stack<Route> _pendingPause = new Stack<Route>();

        // One request per route at a time. A second requester arriving while the route is still deciding
        // shares the pending answer instead of asking again, so a route is never asked twice about the
        // same moment and two requesters never race each other to the same pop.
        private readonly Dictionary<Route, Task<PopOutcome>> _pendingRequests =
            new Dictionary<Route, Task<PopOutcome>>();

        private Task _task = Task.CompletedTask;
        private bool _processing;

        public override WidgetViewReference View { get; }
            = WidgetViewReference.Resource("$$_Navigator");

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
        ///     The routes on the stack, topmost first: a fresh snapshot after every push, pop and replace,
        ///     so a reaction over it fires per mutation (coalesced per frame, like every atom).
        /// </summary>
        /// <remarks>
        ///     A snapshot rather than the live collection because of how this atom is consumed. A computed
        ///     atom compares its new value with the cached one and swallows a change that compares equal;
        ///     the stack used to hand out one collection instance for its whole life, so every mutation
        ///     compared equal and this property never obsoleted a subscriber. It was correct to read during
        ///     a build, which re-reads the contents, and useless to react to. Handing out a new instance per
        ///     mutation is what makes it honest.
        /// </remarks>
        [Atom]
        public IReadOnlyCollection<Route> NavigationStack => _stack.Routes;

        [Atom]
        public Route TopmostRoute => _stack.TopmostRoute;

        /// <summary>
        ///     How deep the stack is and what is on top of it: <c>"3 routes: room-edit"</c>.
        /// </summary>
        /// <remarks>
        ///     Straight off <see cref="NavigatorStack"/> rather than through the atoms above, because a
        ///     label must survive being read at moments they do not cover: <c>Count</c> is a plain read,
        ///     while <c>TopmostRoute</c> peeks and throws on the empty stack this state has between its
        ///     constructor and <c>InitState</c>.
        /// </remarks>
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
        ///     Ends every route still on the stack before the state tree takes them apart.
        /// </summary>
        /// <remarks>
        ///     Without this, unmounting a navigator disposes its routes without ever destroying them: the
        ///     widgets leave the tree, <c>Builder.OnDispose</c> reaches <c>Route.Dispose</c>, and the state
        ///     machine is simply abandoned wherever it stood. Nothing runs <c>OnDestroy</c>, so nothing
        ///     completes <c>PopTask</c>, and every caller awaiting one waits forever. Pop, PopTo and
        ///     Replace all destroy a route before it is disposed; this is what makes unmount agree with
        ///     them.
        ///     <para>
        ///         Safe to do synchronously because every handler on the teardown path completes
        ///         synchronously by construction, which is the reason teardown is its own event rather than
        ///         a reuse of Destroy. Dispose cannot await, and Destroy can block on an exit animation.
        ///     </para>
        /// </remarks>
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
            if (route == null) throw new ArgumentNullException(nameof(route));

            // Attached as soon as the push is issued, not when the command runs: the command may sit
            // behind an exit animation, and an owner that pops its route in that window -- a state
            // disposing right after it pushed -- must find a navigator to queue the pop with.
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
        ///     routes it removes: this is how the app's root changes hands, and a route is no more consulted
        ///     about it than about the navigator unmounting. Everything removed completes with
        ///     <see cref="PopRequest.Teardown"/>.
        /// </summary>
        public Route NewRoot(Route route)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));

            route.AttachTo(this);
            ApplyCommands(
                new NavigatorCommand.PopTo(null),
                new NavigatorCommand.Replace(route, null, PopResult.None(PopRequest.Teardown)));
            return route;
        }

        public Route ReplaceNamed(string routeName)
        {
            var route = CreateRoute(routeName);
            return Replace(route);
        }

        /// <summary>
        ///     Swaps the topmost route for <paramref name="route"/> without asking it. Teardown-class, like
        ///     <see cref="NewRoot"/>: the removed route completes with <see cref="PopRequest.Teardown"/>.
        ///     To swap a route that gets a say, use <see cref="RequestReplace"/>.
        /// </summary>
        public Route Replace(Route route)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));

            route.AttachTo(this);
            ApplyCommands(new NavigatorCommand.Replace(route, null, PopResult.None(PopRequest.Teardown)));
            return route;
        }

        /// <summary>
        ///     Asks <paramref name="route"/> whether it may be popped and, if it agrees, pops it carrying
        ///     <paramref name="request"/> and whatever value the route attached to its answer.
        /// </summary>
        /// <remarks>
        ///     The question is put outside the command loop, so the route may take its time and may
        ///     navigate while deciding; the pop itself is then queued and lands only if the route is still
        ///     topmost, which is what <see cref="PopOutcome.NotTopmost"/> reports. Nothing else on the
        ///     stack is held still meanwhile: another push may cover the route, and if it does the answer
        ///     is honest about it rather than the pop removing the wrong route. A route already being
        ///     asked is not asked again; the second requester shares the pending outcome.
        ///     <para>
        ///         <paramref name="request"/> is the caller's to define and is carried through untouched: it
        ///         is what the route's hook receives and what ends up as <see cref="PopResult.Request"/>.
        ///     </para>
        /// </remarks>
        public Task<PopOutcome> RequestPop(Route route, object request)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));

            if (route.Navigator != this || Topmost() != route)
            {
                return Task.FromResult(PopOutcome.NotTopmost);
            }

            if (_pendingRequests.TryGetValue(route, out var pending))
            {
                return pending;
            }

            var task = RunRequest(route, request, verdict => PopRoute(route, verdict.ToResult(request)));
            RememberRequest(route, task);
            return task;
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
            if (outgoing == null) throw new ArgumentNullException(nameof(outgoing));
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));

            if (outgoing.Navigator != this || Topmost() != outgoing)
            {
                return Task.FromResult(PopOutcome.NotTopmost);
            }

            if (_pendingRequests.TryGetValue(outgoing, out var pending))
            {
                // Someone else is already closing it. Whatever they get, this replace did not happen:
                // either the route refused, or it left through their pop and the incoming route was
                // never pushed. Report the latter as NotTopmost so the caller falls back to a plain push.
                return MapJoined(pending);
            }

            var task = RunRequest(outgoing, request, verdict => ReplaceRoute(outgoing, incoming, verdict.ToResult(request)));
            RememberRequest(outgoing, task);
            return task;
        }

        /// <summary>
        ///     Pops route after route, asking each, until <paramref name="target"/> is on top -- or, when
        ///     <paramref name="target"/> is null, until one route is left. Stops at the first route that
        ///     refuses or that is no longer where the walk expected it, and says which.
        /// </summary>
        /// <remarks>
        ///     One request per route, in turn, rather than one command that removes them all: each route is
        ///     asked while it is genuinely topmost, which is the only moment its answer is about anything
        ///     real. Between two steps the revealed route is resumed and focused as after any pop.
        /// </remarks>
        public async Task<PopToOutcome> RequestPopTo(Route target, object request)
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

        private Task<PopOutcome> ReplaceRoute(Route outgoing, Route incoming, PopResult outgoingResult)
        {
            var command = new NavigatorCommand.Replace(incoming, outgoing, outgoingResult);
            ApplyCommands(command);
            return command.Outcome.Task;
        }

        private async Task<PopOutcome> RunRequest(Route route, object request, Func<PopVerdict, Task<PopOutcome>> commit)
        {
            try
            {
                var verdict = await route.DecideAsync(request);

                if (!verdict.IsAllowed)
                {
                    return PopOutcome.Refused;
                }

                return await commit(verdict);
            }
            finally
            {
                _pendingRequests.Remove(route);
            }
        }

        private void RememberRequest(Route route, Task<PopOutcome> task)
        {
            // Only while it is still pending: RunRequest clears the slot in its finally, and for a decision
            // that completed synchronously that has already happened by the time we get here.
            if (!task.IsCompleted)
            {
                _pendingRequests[route] = task;
            }
        }

        private static async Task<PopOutcome> MapJoined(Task<PopOutcome> pending)
        {
            var outcome = await pending;
            return outcome == PopOutcome.Popped ? PopOutcome.NotTopmost : outcome;
        }

        /// <summary>
        ///     The route on top, asked without tracking: this is control flow, not something a
        ///     computation should come to depend on. Null on an empty stack rather than the throw
        ///     <see cref="TopmostRoute"/> reserves for it.
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
        /// <remarks>
        ///     Whether a loop is running is tracked by a flag rather than read off <c>_task</c>, because
        ///     <c>_task</c> is not assigned until <see cref="ProcessCommandsLoop"/> reaches its first real
        ///     await, and an operation whose every step completes synchronously never gets there. Anything
        ///     that navigates from inside that window -- an observer callback, a route's
        ///     <c>OnInitialize</c>, any handler the loop calls before it first yields -- would find
        ///     <c>_task</c> still holding the previous, completed loop and start a second one on top of the
        ///     first. Both then walk the same stack, and the inner one acts on routes the outer one has
        ///     only half moved: pushing from a <c>DidPush</c> reached a route that was on the stack but not
        ///     yet created, and tried to pause it.
        /// </remarks>
        private void ApplyCommands([NotNull] params NavigatorCommand[] commands)
        {
            _pendingCommands.Enqueue(commands);

            if (_processing)
            {
                return;
            }

            _task = ProcessCommandsLoop();
        }

        private async Task ProcessCommandsLoop()
        {
            // Set before the first await, so the synchronous prefix of the loop is covered too. That
            // prefix is the whole of a navigation whose handlers all complete synchronously, which is
            // every navigation that does not animate.
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
                // A transition abandoned rather than failed, which is not an error worth reporting. The
                // only producer is a route's exit animation being cancelled when the tree disposes the
                // lifetime it is bound to, so this happens on every unmount that lands mid-transition --
                // an ordinary shutdown, printed as a red exception. Console noise on a routine path is
                // worse than useless: it teaches people to stop reading the console.
                //
                // Caught separately rather than filtered inside the general handler so that control flow
                // is provably unchanged: both catches sit outside the loop, so either way the remaining
                // queued commands are abandoned.
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

        private async Task ProcessCommands([NotNull] NavigatorCommand[] commands)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));

            foreach (var command in commands)
            {
                await ProcessCommand(command);
            }

            if (AutoFocus && _stack.Count > 0)
            {
                await _stack.Peek().ApplyScreenEvent(ScreenEvent.Focus);
            }
        }

        private Task ProcessCommand([NotNull] NavigatorCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            switch (command)
            {
                case NavigatorCommand.Pop pop:
                    return PopInternal(pop);

                case NavigatorCommand.PopTo backTo:
                    return PopToInternal(backTo);

                case NavigatorCommand.Push forward:
                    return PushInternal(forward);

                case NavigatorCommand.Replace replace:
                    return ReplaceInternal(replace);
            }

            Debug.LogWarning($"Unexpected navigator command: {command.GetType().Name}");
            return Task.CompletedTask;
        }

        private async Task PushInternal(NavigatorCommand.Push push)
        {
            var screen = push.Route;
            var observers = SnapshotObservers();
            var covered = _stack.Count > 0 ? _stack.Peek() : null;

            // The route learns where it lives before anything else happens to it, so even OnInitialize can
            // already reach the navigator it is being pushed onto.
            screen.AttachTo(this);

            // Announced before the route is built rather than after. Initialization is the only step of a
            // push that can take arbitrarily long -- a route that preloads holds this interval open for as
            // long as it likes -- so a bracket drawn after it would span nothing but the pause of the
            // routes below, which Route's own lifecycle channel already reports in more detail. The cost
            // is that a push whose initialization fails leaves WillPush unmatched; replace cannot avoid
            // that case at all, so the guarantee was never available across the whole interface, and
            // buying it here would have cost the only interval worth announcing.
            NotifyWillPush(observers, screen, covered);

            // Built before anything already on screen is disturbed. OnInitialize is virtual, async and
            // supplied by the route, so it is the one step here that can fail or take arbitrarily long,
            // and doing it first means a route that cannot be built leaves the navigator exactly as it
            // was instead of part-way through a transition. It also stops the screen the user is looking
            // at being paused while the next one loads.
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

            // After the mutation, never before: NavigationStack and TopmostRoute are atoms an observer can
            // read from inside its own callback, and announcing first would make the two channels
            // contradict each other for the length of the call.
            NotifyDidPush(observers, screen, covered);

            await screen.ApplyScreenEvent(ScreenEvent.Create);
        }

        /// <summary>
        ///     Whether the topmost route currently holds focus, asked without depending on the answer.
        /// </summary>
        /// <remarks>
        ///     <see cref="ScreenState"/> is an atom now, and this is control flow rather than observation:
        ///     the very next thing this push does is move that route out of the state it just read. Taken
        ///     outside tracking so that pushing from inside a computation cannot make the computation
        ///     depend on a route's lifecycle, matching <see cref="NavigatorStack"/>, whose <c>Peek</c> and
        ///     <c>Count</c> are deliberately untracked beside its atom-backed public properties.
        /// </remarks>
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

            // A replace with nothing to replace is a push, and which of the two this is can be settled
            // before anything moves, so the observer hears the operation that actually happens instead of
            // a replace with no old route to name.
            var replaced = _stack.Count > 0 ? _stack.Peek() : null;

            // A requested replace names the route it was agreed with. If that route is no longer on top
            // by the time the command runs -- it left through its own pop, or something was pushed over
            // it while it was deciding -- the swap it agreed to no longer exists, and pushing the incoming
            // route anyway would replace a stranger. Nothing happens, and the requester is told so.
            if (replace.Target != null && replaced != replace.Target)
            {
                replace.Outcome.TrySetResult(PopOutcome.NotTopmost);
                return;
            }

            screen.AttachTo(this);

            // Before the route is built, on the same reasoning as PushInternal, and unmatched on the same
            // terms. Replace has that exposure regardless of where the bracket is drawn: its removal is
            // deliberately not committed against a failing destroy, so it can abort before ever reaching
            // the push half.
            if (replaced != null)
            {
                NotifyWillReplace(observers, screen, replaced);
            }
            else
            {
                NotifyWillPush(observers, screen, null);
            }

            // Built before the outgoing route is touched, which is what makes a replace atomic. Building
            // it last meant the old route had already been destroyed and popped by the time the new one
            // was initialized, so a route that could not be built left the navigator permanently one
            // shorter with nothing in its place.
            await InitializeScreen(screen);

            if (_stack.Count > 0)
            {
                // What the outgoing route reports it left with: the agreed answer for a requested replace,
                // the teardown marker for an un-asked one. Set before the destroy so OnDestroy sees it.
                _stack.Peek().SetPopResult(replace.OutgoingResult);

                // The removal is deliberately not committed the way PopInternal and PopToInternal commit
                // theirs. Those two cannot empty the stack, since both refuse below depth 1, whereas a
                // replace removes before it adds: committing against a failing destroy would abort the
                // command with an empty navigator and a TopmostRoute that throws, which is worse than the
                // route it would have left behind.
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

            // Answered once the swap is on the stack, before the incoming route is created: the requester
            // asked whether the outgoing route went, and it has.
            replace.Outcome.TrySetResult(PopOutcome.Popped);

            await screen.ApplyScreenEvent(ScreenEvent.Create);
        }

        private async Task PopToInternal(NavigatorCommand.PopTo popTo)
        {
            // One snapshot for the whole command, not one per iteration: a PopTo is a single operation
            // that happens to remove several routes, and every route it removes must be announced to the
            // same set of observers.
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

                // Un-asked by construction: this is NewRoot clearing the way, and the routes it removes
                // report the same ending as they would at teardown.
                first.SetPopResult(PopResult.None(PopRequest.Teardown));

                var destroyTask = first.ApplyScreenEvent(ScreenEvent.Destroy);

                if (first.ModalType == RouteModalType.Fullscreen)
                {
                    await UnpauseScreens(skip: 1);
                }

                // Per iteration, for the reason given in PopInternal. A throw part-way through still
                // leaves every route it already removed removed, and stops at the one that failed.
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
                pop.Outcome.TrySetResult(_stack.Count == 1 && _stack.Peek() == pop.Target
                    ? PopOutcome.LastRoute
                    : PopOutcome.NotTopmost);
                return;
            }

            var first = _stack.Peek();

            // Pops name their target. One issued for a route that has since left, or been covered, does
            // nothing rather than removing whatever happens to be on top now.
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

            // The removal commits even if the transition does not. TriggerStateMachine assigns the next
            // state before running the handler, so by the time this awaits, the route already reports
            // Destroyed -- and it is still on the stack, still rendered, and still what TopmostRoute,
            // HandleBack and PopIfTopmost answer with. Letting a throw skip the pop leaves the machine and
            // the stack permanently disagreeing about a route that has already said it is finished.
            //
            // Removals commit; additions do not. The mirror of this on the push side would be wrong:
            // nothing has committed before a push, so a throw there should correctly leave it undone.
            try
            {
                await destroyTask;
            }
            finally
            {
                _stack.Pop();

                // In the finally, so a pop that commits against a failed transition is still announced.
                // Every WillPop is matched by a DidPop for exactly that reason.
                NotifyDidPop(observers, first, revealed);

                // Likewise: the route is off the stack, which is what the outcome reports, whatever its
                // transition did on the way.
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
        ///     The observers an operation will announce to, fixed for the whole of it.
        /// </summary>
        /// <remarks>
        ///     Observers are configuration on the widget, and an operation spans awaits during which the
        ///     widget can be replaced. Reading the list again at the Did edge would let an observer hear
        ///     the end of something it never heard the start of, and let another hear the start of
        ///     something it never hears the end of.
        ///     <para>
        ///         Untracked, because <c>Widget</c> is an atom and this read is the navigator's own
        ///         bookkeeping. A navigation started from inside a computation -- a reaction that routes on
        ///         some condition is an ordinary thing to write -- would otherwise come away with a
        ///         dependency on the navigator's widget purely because notifying is a thing the navigator
        ///         now does, and rebuild whenever that widget was replaced.
        ///     </para>
        /// </remarks>
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

        // Every callback is dispatched the same way, and the shape is the point.
        //
        // Iterate the snapshot the operation began with, and contain each observer separately. An observer
        // that throws is logged and the rest still hear the callback, matching how Zone contains a ticker
        // that throws -- the navigator's job is to navigate, and a consumer of notifications must not be
        // able to stop it. The snapshot is also what makes an observer that unregisters itself
        // mid-callback harmless, since what is being walked is no longer the list it removed itself from.
        //
        // The whole walk is untracked, so that observing cannot perturb what it observes. An operation
        // reaches here still on the caller's stack whenever its handlers complete synchronously, which is
        // every navigation that does not animate, and navigating from inside a computation -- a reaction
        // that routes on some condition -- is an ordinary thing to write. Without this, an observer that
        // read NavigationStack or a route's ScreenState in its callback would silently graft that
        // dependency onto that computation, and the next navigation would invalidate it: a reaction would
        // start re-running because something was listening, which is the one thing a listener must never
        // cause. Same reasoning as ClickExtensions, which runs every bound handler under NoWatch.

        private void NotifyWillPush(INavigatorObserver[] observers, Route route, Route previousRoute)
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

        private void NotifyWillReplace(INavigatorObserver[] observers, Route newRoute, Route oldRoute)
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

        private void NotifyDidReplace(INavigatorObserver[] observers, Route newRoute, Route oldRoute)
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
        ///     Removes <see cref="Target"/> if it is on top when the command runs, and says what happened.
        /// </summary>
        public sealed class Pop : NavigatorCommand
        {
            public Route Target { get; }
            public PopResult Result { get; }
            public TaskCompletionSource<PopOutcome> Outcome { get; } =
                new TaskCompletionSource<PopOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Pop([NotNull] Route target, PopResult result)
            {
                Target = target;
                Result = result;
            }
        }

        /// <summary>
        ///     Un-asked removal down to a route, or to the last one when <see cref="Route"/> is null. Only
        ///     <c>NewRoot</c> issues it.
        /// </summary>
        public sealed class PopTo : NavigatorCommand
        {
            public Route Route { get; }

            public PopTo([CanBeNull] Route route) => Route = route;
        }

        public class Push : NavigatorCommand
        {
            public Route Route { get; }

            public Push([NotNull] Route route) => Route = route;
        }

        /// <summary>
        ///     Swaps the topmost route for <see cref="Route"/>. With a <see cref="Target"/>, only if that
        ///     is what is on top; without one, whatever is on top, un-asked. <see cref="OutgoingResult"/> is
        ///     what the removed route reports.
        /// </summary>
        public class Replace : NavigatorCommand
        {
            public Route Route { get; }
            [CanBeNull] public Route Target { get; }
            public PopResult OutgoingResult { get; }
            public TaskCompletionSource<PopOutcome> Outcome { get; } =
                new TaskCompletionSource<PopOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Replace([NotNull] Route route, [CanBeNull] Route target, PopResult outgoingResult)
            {
                Route = route;
                Target = target;
                OutgoingResult = outgoingResult;
            }
        }
    }

    internal class NavigatorStack : IEnumerable<Route>
    {
        private readonly Stack<Route> _stack = new Stack<Route>();
        private readonly List<Widget> _widgets = new List<Widget>();
        private readonly MutableAtom<int> _version = Atom.Value(int.MinValue);

        /// <summary>
        ///     The version counter itself, kept beside the atom that publishes it.
        /// </summary>
        /// <remarks>
        ///     Counted here rather than as <c>_version.Value++</c>, because that is a read as well as a
        ///     write: the getter subscribes whatever computation is running, and the setter then
        ///     invalidates it. Mutating the stack from inside a computation therefore made that computation
        ///     depend on the stack and immediately obsoleted it, so it re-ran for a change it had made
        ///     itself. Publishing a value computed outside the atom leaves the write a write.
        /// </remarks>
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
        ///     nothing underneath.
        /// </summary>
        /// <remarks>
        ///     Untracked, like <see cref="Peek"/> and <see cref="Count"/>: this answers a question asked
        ///     while the stack is being mutated, not one a widget observes. Walks the stack's own struct
        ///     enumerator and stops at the second entry, so asking costs no allocation.
        /// </remarks>
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
            _widgets.Add(new Builder(screen.Build)
            {
                Key = Key.Of(screen),
                OnDispose = screen.Dispose,
            });
            _stack.Push(screen);
            _version.Value = ++_revision;
        }

        public IEnumerator<Route> GetEnumerator() => _stack.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}