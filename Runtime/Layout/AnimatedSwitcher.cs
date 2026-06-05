using System.Linq;
using System.Collections.Generic;
using UniMob.UI.Internal;
using UnityEngine.Assertions;
using UniMob.UI.Widgets;

namespace UniMob.UI.Layout
{
    public class AnimatedSwitcher : StatefulWidget
    {
        public Widget Child { get; set; }
        public float Duration { get; set; }
        public float ReverseDuration { get; set; }
        public AnimatedSwitcherTransitionMode TransitionMode { get; set; } = AnimatedSwitcherTransitionMode.Parallel;
        public AnimatedSwitcherTransitionBuilder TransitionBuilder { get; set; } = DefaultTransitionBuilder;
        public AnimatedSwitcherLayoutBuilder LayoutBuilder { get; set; } = DefaultLayoutBuilder;

        public override State CreateState() => new AnimatedSwitcherState();

        private static Widget DefaultTransitionBuilder(IAnimation<float> animation, Widget child)
        {
            return new Opacity
            {
                OpacityValue = animation,
                Child = child,
            };
        }

        private static Widget DefaultLayoutBuilder(Widget currentChild, IEnumerable<Widget> previousChildren)
        {
            var stack = new ZStack
            {
                Alignment = Alignment.Center,
                Children = {previousChildren}
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
        public override Widget Build(BuildContext context)
        {
            return new UniMob.UI.Widgets.AnimatedSwitcher()
            {
                Child = Widget.Child,
                Duration = Widget.Duration,
                ReverseDuration = Widget.ReverseDuration,
                TransitionMode = Widget.TransitionMode,
                TransitionBuilder = Widget.TransitionBuilder,
                LayoutBuilder = Widget.LayoutBuilder  
            };
        }
    }
}