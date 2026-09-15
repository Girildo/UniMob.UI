using System;
using UniMob.UI.Rendering;
using UnityEngine;

namespace UniMob.UI.Widgets
{
    /// <summary>
    ///     When a scrollbar is shown.
    /// </summary>
    public enum ScrollbarVisibility
    {
        /// <summary>Shown for as long as it is mounted.</summary>
        Always,

        /// <summary>Shown while the attached scrollable moves, and faded out once it stops.</summary>
        WhileScrolling,
    }

    /// <summary>
    ///     A scrollbar bound to a <see cref="ScrollController" />, placed by the caller: over the list
    ///     in a <see cref="ZStack" /> with a <see cref="Positioned" />, or beside it in a
    ///     <see cref="Row" />. It fills its box along <see cref="Axis" /> and is
    ///     <see cref="Thickness" /> across unless its constraints say otherwise.
    /// </summary>
    /// <remarks>
    ///     Draws nothing and takes no input while the attached scrollable's content fits its viewport,
    ///     or while the bar is hidden. The thumb is draggable, and a tap on the track pages by one
    ///     viewport toward the tap while <see cref="PageOnTrackTap" /> is set.
    /// </remarks>
    public class Scrollbar : StatefulWidget
    {
        /// <summary>
        ///     The controller whose attached scrollable this bar describes and drives. Required: a
        ///     scrollbar given none throws when it is mounted.
        /// </summary>
        public ScrollController Controller { get; init; } = null!;

        /// <summary>The track's orientation. The controller's metrics are read along it.</summary>
        public Axis Axis { get; init; } = Axis.Vertical;

        /// <summary>The track's extent across <see cref="Axis" />, where the constraints leave it loose.</summary>
        public float Thickness { get; init; } = 8f;

        /// <summary>The widget drawn as the thumb. A translucent grey box when left unset.</summary>
        public Widget? Thumb { get; init; }

        /// <summary>The shortest the thumb may be drawn, so that it stays grabbable on long content.</summary>
        public float MinThumbExtent { get; init; } = 18f;

        /// <summary>
        ///     The shortest the thumb may be drawn while the scrollable is overscrolled. Defaults to
        ///     <see cref="MinThumbExtent" />.
        /// </summary>
        public float? MinOverscrollThumbExtent { get; init; }

        public ScrollbarVisibility Visibility { get; init; } = ScrollbarVisibility.WhileScrolling;

        /// <summary>Seconds the bar stays up after the scrollable stops moving.</summary>
        public float FadeDelay { get; init; } = 0.6f;

        /// <summary>Seconds the fade itself takes, in both directions.</summary>
        public float FadeDuration { get; init; } = 0.3f;

        /// <summary>Whether a tap on the track pages by one viewport toward the tap.</summary>
        public bool PageOnTrackTap { get; init; } = true;

        public override State CreateState() => new ScrollbarState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state) =>
            new RenderScrollbar((ScrollbarState)state);

        public override string GetDiagnosticInfo() => $"{Axis} {Visibility}";
    }

    internal class ScrollbarState : ViewState<Scrollbar>, IScrollbarState
    {
        private static readonly Color DefaultThumbColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);

        private readonly StateHolder _thumb;
        private bool _dragging;

        // Created in InitState, which runs before anything can read the state.
        private AnimationController _fade = null!;

        private float _idleElapsed;
        private bool _idleTickerRegistered;

        public ScrollbarState()
        {
            _thumb = CreateChild(_ => BuildThumb());
        }

        public IState? Child => _thumb.Value;

        [Atom]
        public ScrollMetrics? Metrics => Widget.Controller.Metrics;

        public Axis Axis => Widget.Axis;

        public float Thickness => Widget.Thickness;

        public float MinThumbExtent => Widget.MinThumbExtent;

        public float MinOverscrollThumbExtent =>
            Widget.MinOverscrollThumbExtent ?? Widget.MinThumbExtent;

        public IAnimation<float> Opacity => _fade;

        [Atom]
        public bool BlocksPointer => !IsHidden;

        [Atom]
        public Action<TapDetails>? OnTrackTap =>
            Widget.PageOnTrackTap && !IsHidden ? HandleTrackTap : null;

        public override WidgetViewReference View =>
            WidgetViewReference.Registered("UniMob.ScrollbarView");

        private bool IsHidden => _fade.IsDismissed;

        public override void InitState()
        {
            base.InitState();

            if (Widget.Controller == null)
            {
                throw new ArgumentNullException(
                    nameof(Scrollbar.Controller),
                    "A Scrollbar draws the scrollable a ScrollController is attached to, so it needs "
                        + "the same controller the ScrollList was given."
                );
            }

            _fade = new AnimationController(
                StateLifetime,
                Widget.FadeDuration,
                completed: Widget.Visibility == ScrollbarVisibility.Always
            );

            // fireImmediately: false, because creating the reaction is not a scroll. Showing the bar
            // on the creation run would put one on screen on every mount of a list nobody touched.
            Atom.Reaction(
                StateLifetime,
                () => Widget.Controller.PixelOffset,
                _ =>
                {
                    if (Widget.Visibility == ScrollbarVisibility.WhileScrolling)
                    {
                        Show();
                    }
                },
                fireImmediately: false,
                debugName: "ScrollbarState.ShowWhileScrolling"
            );

            StateLifetime.Register(StopIdleCountdown);
        }

        public override void DidUpdateWidget(Scrollbar oldWidget)
        {
            base.DidUpdateWidget(oldWidget);

            // FadeDelay needs no such handling: the countdown reads it off the current widget on
            // every tick.
            if (!Mathf.Approximately(oldWidget.FadeDuration, Widget.FadeDuration))
            {
                _fade.Duration = Widget.FadeDuration;
                _fade.ReverseDuration = Widget.FadeDuration;
            }

            if (oldWidget.Visibility == Widget.Visibility)
            {
                return;
            }

            if (Widget.Visibility == ScrollbarVisibility.Always)
            {
                StopIdleCountdown();
                _fade.Complete();
            }
            else
            {
                _fade.Dismiss();
            }
        }

        public override string GetDiagnosticInfo()
        {
            var metrics = MetricsOrNull();

            return $"{base.GetDiagnosticInfo()} "
                + (
                    metrics.HasValue
                        ? $"offset {metrics.Value.PixelOffset:0.#}/{metrics.Value.ContentExtent:0.#}, "
                            + $"viewport {metrics.Value.ViewportExtent:0.#}"
                        : "no metrics"
                );
        }

        private Widget BuildThumb() =>
            new GestureDetector
            {
                OnDragStart = HandleDragStart,
                OnDragUpdate = HandleDragUpdate,
                OnDragEnd = HandleDragEnd,
                Child = Widget.Thumb ?? new ColoredImageBox { Color = DefaultThumbColor },
            };

        private void HandleDragStart(DragDetails details)
        {
            _dragging = true;
            Show();

            // Landing on the offset the bar was drawn from kills any inertia or running animation,
            // which would otherwise fight every update of this drag.
            Widget.Controller.JumpTo(Widget.Controller.PixelOffset);
        }

        private void HandleDragUpdate(DragDetails details)
        {
            var render = (RenderScrollbar)RenderObject;

            if (render.Thumb is not { } thumb || Widget.Controller.Metrics is not { } metrics)
            {
                return;
            }

            var horizontal = Widget.Axis == Axis.Horizontal;
            var trackExtent = horizontal ? render.PeekSize().x : render.PeekSize().y;
            var thumbDelta = horizontal ? details.LocalDelta.x : details.LocalDelta.y;

            var pixelDelta = ScrollbarGeometry.PixelDeltaForThumbDelta(
                metrics,
                trackExtent,
                thumb.Extent,
                thumbDelta
            );

            Widget.Controller.JumpTo(metrics.PixelOffset + pixelDelta);
        }

        private void HandleDragEnd(DragDetails details)
        {
            _dragging = false;
        }

        private void HandleTrackTap(TapDetails details)
        {
            var render = (RenderScrollbar)RenderObject;

            if (render.Thumb is not { } thumb || Widget.Controller.Metrics is not { } metrics)
            {
                return;
            }

            var position =
                Widget.Axis == Axis.Horizontal ? details.LocalPosition.x : details.LocalPosition.y;

            if (position >= thumb.Offset && position <= thumb.Offset + thumb.Extent)
            {
                return;
            }

            Widget.Controller.JumpTo(
                ScrollbarGeometry.PageTarget(metrics, towardStart: position < thumb.Offset)
            );

            Show();
        }

        private void Show()
        {
            // An Always bar is already up and never counts down, so it registers no ticker at all:
            // one that merely removed itself on its first tick would leave the clock unsettled for a
            // frame after every scroll.
            if (Widget.Visibility == ScrollbarVisibility.Always)
            {
                return;
            }

            _fade.Forward();
            _idleElapsed = 0f;

            if (_idleTickerRegistered)
            {
                return;
            }

            _idleTickerRegistered = true;
            Zone.Current.AddTicker(Tick);
        }

        private void Tick(float deltaTime)
        {
            if (_dragging)
            {
                _idleElapsed = 0f;
                return;
            }

            _idleElapsed += deltaTime;

            if (_idleElapsed < Widget.FadeDelay)
            {
                return;
            }

            StopIdleCountdown();
            _fade.Reverse();
        }

        private void StopIdleCountdown()
        {
            if (!_idleTickerRegistered)
            {
                return;
            }

            _idleTickerRegistered = false;
            Zone.Current?.RemoveTicker(Tick);
        }

        // Tolerant of a disposed state and of never having been laid out, and reads nothing
        // reactively: a label is read mid-layout and from a debugger.
        private ScrollMetrics? MetricsOrNull()
        {
            if (StateLifetime.IsDisposed)
            {
                return null;
            }

            using (Atom.NoWatch)
            {
                return Widget?.Controller?.Metrics;
            }
        }
    }
}
