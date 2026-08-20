using System.Collections.Generic;
using System.Linq;
using UniMob.UI.Internal;
using UniMob.UI.Widgets;
using UnityEngine.Assertions;

namespace UniMob.UI.Widgets
{
    /// <summary>
    /// Animates between children: when <see cref="Child"/> is replaced by one that cannot update it
    /// in place, the old child animates out while the new one animates in. An outgoing child stays
    /// mounted until its reverse animation reaches <see cref="AnimationStatus.Dismissed"/>.
    /// </summary>
    public class AnimatedSwitcher : StatefulWidget
    {
        public Widget? Child { get; init; }
        public float Duration { get; init; }
        public float ReverseDuration { get; init; }
        public AnimatedSwitcherTransitionMode TransitionMode { get; init; } =
            AnimatedSwitcherTransitionMode.Parallel;
        public AnimatedSwitcherTransitionBuilder TransitionBuilder { get; init; } =
            DefaultTransitionBuilder;
        public AnimatedSwitcherLayoutBuilder LayoutBuilder { get; init; } = DefaultLayoutBuilder;

        public override State CreateState() => new AnimatedSwitcherState();

        private static Widget DefaultTransitionBuilder(IAnimation<float> animation, Widget child)
        {
            return new Opacity { OpacityValue = animation, Child = child };
        }

        private static Widget DefaultLayoutBuilder(
            Widget? currentChild,
            IEnumerable<Widget> previousChildren
        )
        {
            var stack = new ZStack
            {
                Alignment = Alignment.Center,
                Children = { previousChildren },
            };

            if (currentChild != null)
            {
                stack.Children.Add(currentChild);
            }

            return stack;
        }
    }

    public class AnimatedSwitcherState : HocState<AnimatedSwitcher>
    {
        private readonly MutableAtom<int> _version = Atom.Value(int.MinValue);
        private readonly List<Entry> _outgoingEntries = new List<Entry>();
        private readonly Queue<AnimationController> _pendingAnimations =
            new Queue<AnimationController>();

        private List<Widget>? _outgoingWidgets = new List<Widget>();
        private Entry? _currentEntry;
        private int _childNumber;

        public override void InitState()
        {
            base.InitState();

            AddEntryForNewChild(animate: false);
        }

        public override Widget Build(BuildContext context)
        {
            _version.Get();

            var outgoingWidgets = RebuildOutgoingWidgetsIfNeed();

            var previousChildren = outgoingWidgets.Where(w =>
                w.Key != _currentEntry?.Transition.Key
            );
            return Widget.LayoutBuilder.Invoke(_currentEntry?.Transition, previousChildren);
        }

        public override void DidUpdateWidget(AnimatedSwitcher oldWidget)
        {
            base.DidUpdateWidget(oldWidget);

            if (Widget.TransitionBuilder != oldWidget.TransitionBuilder)
            {
                foreach (var outgoingEntry in _outgoingEntries)
                {
                    UpdateTransitionForEntry(outgoingEntry);
                }

                if (_currentEntry != null)
                {
                    UpdateTransitionForEntry(_currentEntry);
                }

                MarkChildWidgetCacheAsDirty();
            }

            var newChild = Widget.Child;
            var currentEntry = _currentEntry;

            var needsNewEntry =
                newChild == null
                    ? currentEntry != null
                    : currentEntry == null
                        || !StateUtilities.CanUpdateWidget(newChild, currentEntry.Child);

            if (needsNewEntry)
            {
                _childNumber += 1;
                AddEntryForNewChild(animate: true);
            }
            else if (currentEntry != null && newChild != null)
            {
                currentEntry.Child = newChild;
                UpdateTransitionForEntry(currentEntry);

                MarkChildWidgetCacheAsDirty();
            }
        }

        private void AddEntryForNewChild(bool animate)
        {
            var outgoing = _currentEntry;

            Assert.IsTrue(animate || outgoing == null);

            var hasCurrentEntry = outgoing != null;
            if (outgoing != null)
            {
                Assert.IsTrue(animate);
                Assert.IsTrue(!_outgoingEntries.Contains(outgoing));

                _outgoingEntries.Add(outgoing);

                if (outgoing.AnimationController.Status != AnimationStatus.Dismissed)
                {
                    outgoing.AnimationController.Reverse();
                }
                else
                {
                    outgoing.LifetimeController.Dispose();
                }

                _currentEntry = null;

                MarkChildWidgetCacheAsDirty();
            }

            var child = Widget.Child;
            if (child == null)
            {
                return;
            }

            var lc = StateLifetime.CreateNested();
            var controller = new AnimationController(
                lc.Lifetime,
                Widget.Duration,
                Widget.ReverseDuration
            );

            _currentEntry = NewEntry(lc, child, controller, Widget.TransitionBuilder);

            if (animate)
            {
                if (
                    hasCurrentEntry
                    && Widget.TransitionMode == AnimatedSwitcherTransitionMode.Sequential
                )
                {
                    _pendingAnimations.Enqueue(controller);
                }
                else
                {
                    controller.Forward();
                }
            }
            else
            {
                Assert.IsTrue(_outgoingEntries.Count == 0);
                controller.Complete();
            }
        }

        private Entry NewEntry(
            LifetimeController lc,
            Widget child,
            AnimationController controller,
            AnimatedSwitcherTransitionBuilder transition
        )
        {
            var entry = new Entry(
                lc,
                controller,
                MakeTransition(child, controller, transition),
                child
            );

            Atom.Reaction(
                lc.Lifetime,
                () => controller.Status,
                status =>
                {
                    if (status == AnimationStatus.Dismissed)
                    {
                        Zone.Current.NextFrame(lc.Dispose);
                    }
                },
                fireImmediately: false
            );

            lc.Register(() =>
            {
                _outgoingEntries.Remove(entry);
                _version.Value++;

                MarkChildWidgetCacheAsDirty();

                ForwardPendingAnimation();
            });

            return entry;
        }

        private void ForwardPendingAnimation()
        {
            while (_pendingAnimations.Count > 0)
            {
                var controller = _pendingAnimations.Dequeue();
                if (controller.Lifetime.IsDisposed)
                {
                    continue;
                }

                controller.Forward();
                break;
            }
        }

        private void UpdateTransitionForEntry(Entry entry)
        {
            entry.Transition = MakeTransition(
                entry.Child,
                entry.AnimationController,
                Widget.TransitionBuilder
            );
        }

        private Builder MakeTransition(
            Widget child,
            IAnimation<float> animation,
            AnimatedSwitcherTransitionBuilder transition
        )
        {
            return new Builder(_ => transition.Invoke(animation, child))
            {
                Key = child.Key ?? Key.Of(_childNumber),
            };
        }

        private void MarkChildWidgetCacheAsDirty()
        {
            _outgoingWidgets = null;
        }

        private List<Widget> RebuildOutgoingWidgetsIfNeed()
        {
            var widgets = _outgoingWidgets ??= _outgoingEntries
                .Select(entry => entry.Transition)
                .ToList();

            Assert.IsTrue(_outgoingEntries.Count == widgets.Count);
            Assert.IsTrue(
                _outgoingEntries.Count == 0 || _outgoingEntries.Last().Transition == widgets.Last()
            );

            return widgets;
        }

        private class Entry
        {
            public Entry(
                LifetimeController lifetimeController,
                AnimationController animationController,
                Widget transition,
                Widget child
            )
            {
                LifetimeController = lifetimeController;
                AnimationController = animationController;
                Transition = transition;
                Child = child;
            }

            public readonly LifetimeController LifetimeController;
            public readonly AnimationController AnimationController;
            public Widget Transition;
            public Widget Child;
        }
    }

    public enum AnimatedSwitcherTransitionMode
    {
        Parallel,
        Sequential,
    }

    public delegate Widget AnimatedSwitcherTransitionBuilder(
        IAnimation<float> animation,
        Widget child
    );

    public delegate Widget AnimatedSwitcherLayoutBuilder(
        Widget? currentChild,
        IEnumerable<Widget> previousWidget
    );
}
