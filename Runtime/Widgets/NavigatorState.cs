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

        private Task _task = Task.CompletedTask;

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

            ApplyCommands(new NavigatorCommand.Push(route));
            return route;
        }

        public async Task<TResult> Push<TResult>(Route route)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));

            ApplyCommands(new NavigatorCommand.Push(route));
            var result = await route.PopTask;
            return result is TResult tResult ? tResult : default;
        }

        public Route NewRootNamed(string routeName)
        {
            var route = CreateRoute(routeName);
            return NewRoot(route);
        }

        public Route NewRoot(Route route)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));

            ApplyCommands(
                new NavigatorCommand.PopTo(null),
                new NavigatorCommand.Replace(route));
            return route;
        }

        public Route ReplaceNamed(string routeName)
        {
            var route = CreateRoute(routeName);
            return Replace(route);
        }

        public Route Replace(Route route)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));

            ApplyCommands(new NavigatorCommand.Replace(route));
            return route;
        }

        public void PopTo(Route route)
        {
            //if (route == null) throw new ArgumentNullException(nameof(route));

            ApplyCommands(new NavigatorCommand.PopTo(route));
        }

        public void Pop(object result = null)
        {
            ApplyCommands(new NavigatorCommand.Pop(result));
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

        private void ApplyCommands([NotNull] params NavigatorCommand[] commands)
        {
            _pendingCommands.Enqueue(commands);

            if (_task.IsCompleted)
            {
                _task = ProcessCommandsLoop();
            }
        }

        private async Task ProcessCommandsLoop()
        {
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
                    return PopInternal(pop.Result);

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
            else if (_stack.Count >= 1 && _stack.Peek().ScreenState == ScreenState.Focused)
            {
                await _stack.Peek().ApplyScreenEvent(ScreenEvent.Unfocus);
            }

            _stack.Push(screen);
            await screen.ApplyScreenEvent(ScreenEvent.Create);
        }

        private async Task ReplaceInternal(NavigatorCommand.Replace replace)
        {
            var screen = replace.Route;

            // Built before the outgoing route is touched, which is what makes a replace atomic. Building
            // it last meant the old route had already been destroyed and popped by the time the new one
            // was initialized, so a route that could not be built left the navigator permanently one
            // shorter with nothing in its place.
            await InitializeScreen(screen);

            if (_stack.Count > 0)
            {
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
            await screen.ApplyScreenEvent(ScreenEvent.Create);
        }

        private async Task PopToInternal(NavigatorCommand.PopTo popTo)
        {
            while (_stack.Count > 1)
            {
                if (popTo.Route != null && _stack.Peek().Key == popTo.Route.Key)
                {
                    break;
                }

                var first = _stack.Peek();
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
                }
            }
        }

        private async Task PopInternal(object result)
        {
            if (_stack.Count <= 1)
            {
                return;
            }

            var first = _stack.Peek();

            first.SetResult(result);

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
    }

    internal abstract class NavigatorCommand
    {
        public sealed class Pop : NavigatorCommand
        {
            public object Result { get; }

            public Pop(object result) => this.Result = result;
        }

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

        public class Replace : NavigatorCommand
        {
            public Route Route { get; }

            public Replace([NotNull] Route route) => Route = route;
        }
    }

    internal class NavigatorStack : IEnumerable<Route>
    {
        private readonly Stack<Route> _stack = new Stack<Route>();
        private readonly List<Widget> _widgets = new List<Widget>();
        private readonly MutableAtom<int> _version = Atom.Value(int.MinValue);

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

        public IReadOnlyCollection<Route> Routes
        {
            get
            {
                _version.Get();
                return _stack;
            }
        }


        public Route Peek() => _stack.Peek();

        public Route Pop()
        {
            _widgets.RemoveAt(_widgets.Count - 1);
            var result = _stack.Pop();
            _version.Value++;
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
            _version.Value++;
        }

        public IEnumerator<Route> GetEnumerator() => _stack.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}