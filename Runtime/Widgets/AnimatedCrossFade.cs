using System;

namespace UniMob.UI.Widgets
{
    /// <summary>
    /// Crossfades between two children, driven by <see cref="CrossFadeState"/>. Modern-layout counterpart
    /// of the legacy <c>UniMob.UI.Widgets.AnimatedCrossFade</c>: an <see cref="HocState{TWidget}"/> that owns
    /// the <see cref="AnimationController"/> and composes two modern <see cref="CompositeTransition"/>s
    /// (opacity tweens) inside a <see cref="ZStack"/>. Coexists with the legacy widget; told apart by namespace.
    /// </summary>
    public class AnimatedCrossFade : StatefulWidget
    {
        public Widget FirstChild { get; init; } = SizedBox.Shrink();
        public Widget SecondChild { get; init; } = SizedBox.Shrink();
        public CrossFadeState CrossFadeState { get; init; } = CrossFadeState.ShowFirst;
        public float Duration { get; init; } = 0f;
        public float? ReverseDuration { get; init; } = null;
        public Alignment Alignment { get; init; } = Alignment.Center;
        public bool KeepMounted { get; init; } = false;

        public override State CreateState() => new AnimatedCrossFadeState();

        // Which of the two children this is meant to be showing. Both are in the tree while the fade
        // runs, and (with KeepMounted) after it, so the tree alone never answers this.
        public override string GetDiagnosticInfo() =>
            CrossFadeState == CrossFadeState.ShowFirst ? "first" : "second";

        internal float GetReverseDuration() => ReverseDuration ?? Duration;
    }

    internal class AnimatedCrossFadeState : HocState<AnimatedCrossFade>
    {
        private readonly Key _firstKey = Key.Of(CrossFadeState.ShowFirst);
        private readonly Key _secondKey = Key.Of(CrossFadeState.ShowSecond);

        // All three built by InitState, which runs before the first Build.
        private AnimationController _controller = null!;
        private IAnimation<float> _firstAnimation = null!;
        private IAnimation<float> _secondAnimation = null!;

        public override void InitState()
        {
            base.InitState();

            var completed = Widget.CrossFadeState == CrossFadeState.ShowSecond;
            _controller = new AnimationController(
                StateLifetime,
                Widget.Duration,
                Widget.ReverseDuration,
                completed
            );

            _firstAnimation = _controller.Drive(new FloatTween(1, 0));
            _secondAnimation = _controller.Drive(new FloatTween(0, 1));
        }

        public override Widget Build(BuildContext context)
        {
            return new ZStack
            {
                Alignment = Widget.Alignment,
                Children =
                {
                    BuildLayer(
                        _firstKey,
                        Widget.FirstChild,
                        _firstAnimation,
                        _controller.IsCompleted
                    ),
                    BuildLayer(
                        _secondKey,
                        Widget.SecondChild,
                        _secondAnimation,
                        _controller.IsDismissed
                    ),
                },
            };
        }

        // Unmounts a faded-out layer (unless KeepMounted); the ZStack slot stays stable by index.
        private Widget BuildLayer(
            Key key,
            Widget child,
            IAnimation<float> opacity,
            bool fullyHidden
        )
        {
            if (!Widget.KeepMounted && fullyHidden)
            {
                return SizedBox.Shrink();
            }

            return new CompositeTransition
            {
                Key = key,
                Child = child,
                Opacity = opacity,
            };
        }

        public override void DidUpdateWidget(AnimatedCrossFade oldWidget)
        {
            base.DidUpdateWidget(oldWidget);

            if (Math.Abs(oldWidget.Duration - Widget.Duration) > float.Epsilon)
            {
                _controller.Duration = Widget.Duration;
            }

            if (
                Math.Abs(oldWidget.GetReverseDuration() - Widget.GetReverseDuration())
                > float.Epsilon
            )
            {
                _controller.ReverseDuration = Widget.GetReverseDuration();
            }

            if (oldWidget.CrossFadeState != Widget.CrossFadeState)
            {
                switch (Widget.CrossFadeState)
                {
                    case CrossFadeState.ShowFirst:
                        _controller.Reverse();
                        break;

                    case CrossFadeState.ShowSecond:
                        _controller.Forward();
                        break;
                }
            }
        }
    }
}
