using System;
using System.Collections;
using UniMob.UI.Internal;
using UniMob.UI.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace UniMob.UI.Internal.Views
{
    [RequireComponent(typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D))]
    internal class ScrollListView : View<IScrollingListState>
    {
        [SerializeField]
        private ScrollRect scrollRect = null!;

        [SerializeField]
        private RectTransform contentRoot = null!;

        [SerializeField]
        private RectMask2D rectMask = null!;
        private bool _isUpdatingFromController;

        private ViewMapperBase _mapper = null!;
        private RectTransform _rectTransform = null!;

        protected override void Awake()
        {
            base.Awake();
            _rectTransform = (RectTransform)transform;

            if (scrollRect == null)
                TryGetComponent(out scrollRect);
            if (contentRoot == null)
                contentRoot = scrollRect.content;
            if (rectMask == null)
                TryGetComponent(out rectMask);

            // After contentRoot is resolved, not before: the mapper captures the transform it is
            // given, so building it first parented every child to whatever the prefab left null.
            _mapper = new PooledViewMapper(contentRoot);

            if (scrollRect.horizontal && scrollRect.vertical)
                throw new InvalidOperationException(
                    "ScrollRect cannot be both horizontal and vertical at the same time."
                        + " Please set either horizontal or vertical to true, but not both."
                );

            EnsurePivotAndAnchorsAreConsistentWithDirection(
                scrollRect.horizontal && !scrollRect.vertical
            );

            scrollRect.onValueChanged.AddListener(OnScrollPositionChanged);
        }

        protected override void Activate()
        {
            base.Activate();

            // Set the initial scroll position when the view becomes active.
            var isHorizontal = State.Axis == Axis.Horizontal;
            ApplyPixelOffsetToScrollRect(State.ScrollController.PixelOffset, isHorizontal);

            Atom.Reaction(StateLifetime, () => SyncScrollRectToController(isHorizontal));
        }

        // Moves the ScrollRect onto the controller's pixel offset when the two disagree. The offset is
        // the one the render object builds its window for, so a ScrollRect anywhere else shows a
        // region nothing was built for. Reads the controller's atom, so a reaction calling this
        // tracks it.
        private void SyncScrollRectToController(bool isHorizontal)
        {
            var controllerValue = State.ScrollController.PixelOffset;
            var currentValue = ReadPixelOffsetFromScrollRect(isHorizontal);

            if (Mathf.Abs(currentValue - controllerValue) <= 0.5f)
                return;

            _isUpdatingFromController = true;
            ApplyPixelOffsetToScrollRect(controllerValue, isHorizontal);
            Zone.Current.NextFrame(() => _isUpdatingFromController = false);
        }

        // Total distance, in pixels, the content can scroll along the given axis. The ScrollRect's own
        // normalizedPosition is always a ratio against this value -- since it's the render object's estimated
        // TotalContentSize() for a lazy list, it can change from one call to the next. That's fine here: unlike
        // RenderSliverList's *own* internal math (which used to re-derive "current scroll pixels" from this
        // ratio on every layout pass, even ones nobody scrolled), this conversion only ever runs at the moment
        // Unity's own position actually changed (drag/inertia/elastic-bounce/ScrollTo write), so a shifting
        // denominator here doesn't cause drift -- it just means each real scroll event is measured against the
        // freshest size estimate available at that instant.
        private float GetTotalScrollableDistance(bool isHorizontal)
        {
            var contentSize = contentRoot.rect.size;
            var viewportSize = _rectTransform.rect.size;
            var dist = isHorizontal
                ? contentSize.x - viewportSize.x
                : contentSize.y - viewportSize.y;
            return Mathf.Max(0, dist);
        }

        // Reads the ScrollRect's current position in pixels, via Unity's own normalized-position getter (not
        // raw content.anchoredPosition) so this always agrees with whatever Unity's internal
        // drag/inertia/elastic-bounce bookkeeping currently considers "the" position.
        private float ReadPixelOffsetFromScrollRect(bool isHorizontal)
        {
            var normalized = isHorizontal
                ? scrollRect.horizontalNormalizedPosition
                : 1f - scrollRect.verticalNormalizedPosition;
            return normalized * GetTotalScrollableDistance(isHorizontal);
        }

        // Writes a pixel offset to the ScrollRect via Unity's own normalized-position setter -- not by poking
        // content.anchoredPosition directly -- so Unity's internal drag-velocity/inertia state stays consistent
        // for whatever the user does right after (e.g. grabbing the list mid-ScrollTo-animation).
        private void ApplyPixelOffsetToScrollRect(float pixelOffset, bool isHorizontal)
        {
            var totalScrollableDistance = GetTotalScrollableDistance(isHorizontal);
            var normalized =
                totalScrollableDistance > 0 ? pixelOffset / totalScrollableDistance : 0f;

            if (isHorizontal)
                scrollRect.horizontalNormalizedPosition = normalized;
            else
                scrollRect.verticalNormalizedPosition = 1f - normalized;
        }

        private void EnsurePivotAndAnchorsAreConsistentWithDirection(bool isHorizontal)
        {
            var targetPivot = isHorizontal
                ? new Vector2(0, contentRoot.pivot.y)
                // For vertical, set pivot to the top.
                : new Vector2(contentRoot.pivot.x, 1);

            var targetAnchorMin = isHorizontal ? new Vector2(0, 0) : new Vector2(0, 1);
            var targetAnchorMax = isHorizontal ? new Vector2(0, 1) : new Vector2(1, 1);

            if (
                contentRoot.pivot == targetPivot
                && contentRoot.anchorMin == targetAnchorMin
                && contentRoot.anchorMax == targetAnchorMax
            )
                return;

            // To prevent the content from visually jumping when the pivot changes,
            // we calculate the positional offset caused by the pivot shift
            // and apply an opposite adjustment to the anchoredPosition.
            var originalPivot = contentRoot.pivot;
            var originalSize = contentRoot.rect.size;

            // Apply new anchors and pivot
            contentRoot.anchorMin = targetAnchorMin;
            contentRoot.anchorMax = targetAnchorMax;
            contentRoot.pivot = targetPivot;

            var pivotChange = contentRoot.pivot - originalPivot;
            var positionChange = new Vector2(
                pivotChange.x * originalSize.x,
                pivotChange.y * originalSize.y
            );
            contentRoot.anchoredPosition -= positionChange;
        }

        protected override void Render()
        {
            if (State.RenderObject is not IScrollableRenderObject renderObject)
                return;

            if (rectMask != null)
                rectMask.enabled = State.UseMask;

            if ((int)scrollRect.movementType != (int)State.MovementType)
                scrollRect.movementType = (ScrollRect.MovementType)(State.MovementType);

            var isHorizontal = State.Axis == Axis.Horizontal;
            var axisChanged = scrollRect.horizontal != isHorizontal;

            scrollRect.horizontal = isHorizontal;
            scrollRect.vertical = !isHorizontal;

            if (axisChanged)
            {
                scrollRect.normalizedPosition = isHorizontal
                    ? new Vector2(0, 0)
                    : new Vector2(0, 1);

                var targetPivot = isHorizontal ? new Vector2(0, 0.5f) : new Vector2(0.5f, 1);
                var targetAnchorMin = isHorizontal ? new Vector2(0, 0) : new Vector2(0, 1);
                var targetAnchorMax = isHorizontal ? new Vector2(0, 1) : new Vector2(1, 1);

                contentRoot.anchorMin = targetAnchorMin;
                contentRoot.anchorMax = targetAnchorMax;
                contentRoot.pivot = targetPivot;

                contentRoot.anchoredPosition = Vector2.zero;
                contentRoot.offsetMax = Vector2.zero;
                contentRoot.offsetMin = Vector2.zero;
            }

            // Resize the content size based on the total content size calculated by the render object.
            // with some tolerance.
            var totalContentSize = renderObject.TotalContentSize();

            var currentSize = isHorizontal ? contentRoot.rect.width : contentRoot.rect.height;
            if (Mathf.Abs(currentSize - totalContentSize) > 0.01f)
            {
                contentRoot.sizeDelta = isHorizontal
                    ? new Vector2(totalContentSize, contentRoot.sizeDelta.y)
                    : new Vector2(contentRoot.sizeDelta.x, totalContentSize);

                // A ScrollRect keeps its normalized position across a resize, which moves it in
                // pixels; and a view activated before its content had a size could not apply the
                // controller's offset at all. Either way the window was built for the controller's
                // offset, so the ScrollRect goes back onto it. Unwatched: the reaction started in
                // Activate already follows the controller, and a render pass must not.
                using (Atom.NoWatch)
                {
                    SyncScrollRectToController(isHorizontal);
                }
            }

            using (var render = _mapper.CreateRender())
            {
                var children = State.Children;
                var childrenLayout = renderObject.ChildrenLayout;
                for (var i = 0; i < children.Length; i++)
                {
                    var child = children[i];
                    var layoutData = childrenLayout[i];
                    var childView = render.RenderItem(child);

                    var rt = childView.rectTransform;
                    rt.anchorMin = new Vector2(0, 1);
                    rt.anchorMax = new Vector2(0, 1);

                    var pivotOffset = new Vector2(
                        layoutData.Size.x * rt.pivot.x,
                        -layoutData.Size.y * (1.0f - rt.pivot.y)
                    );

                    rt.sizeDelta = layoutData.Size;

                    rt.anchoredPosition =
                        new Vector2(layoutData.Position.x, -layoutData.Position.y) + pivotOffset;
                }
            }
        }

        private void OnScrollPositionChanged(Vector2 normalizedPosition)
        {
            if (_isUpdatingFromController || !HasState || State.StateLifetime.IsDisposed)
                return;

            var isHorizontal = State.Axis == Axis.Horizontal;
            var normalizedValue = isHorizontal ? normalizedPosition.x : 1 - normalizedPosition.y;

            // Converted once, right here, at the moment Unity tells us the position actually changed
            // (drag/inertia/elastic-bounce/clamp) -- not re-derived on every layout pass. That's what keeps
            // PixelOffset stable against RenderSliverList's estimated TotalContentSize() drifting between
            // scroll events: as long as the user hasn't actually moved, nothing re-runs this conversion.
            State.ScrollController.PixelOffset =
                normalizedValue * GetTotalScrollableDistance(isHorizontal);
        }

        public bool ScrollTo(
            int index,
            float duration,
            ScrollToPosition scrollToPosition,
            Easing? easing
        ) // this should be moved to the controller
        {
            if (State?.RenderObject is not IScrollableRenderObject renderSliver)
                return false;

            var targetPixelOffset = renderSliver.CalculateScrollPixelOffset(
                index,
                scrollToPosition
            );

            // Stop any existing scroll animations before starting a new one.
            StopAllCoroutines();
            StartCoroutine(AnimateScrollTo(targetPixelOffset, duration, easing));
            return true;
        }

        private IEnumerator AnimateScrollTo(float targetPixelOffset, float duration, Easing? easing)
        {
            // Every caller down the chain defaults easing to null; without this the coroutine throws
            // on the first frame of a plain ScrollTo.
            var curve = easing ?? Ease.Linear;

            var originalMovementType = scrollRect.movementType;
            try
            {
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
                var time = 0f;
                var isHorizontal = State.Axis == Axis.Horizontal;

                var startPixelOffset = ReadPixelOffsetFromScrollRect(isHorizontal);

                while (time < duration)
                {
                    // Check for user interruption by comparing the current position with where
                    // our animation left it last frame. If they differ, the user has taken control.
                    var lastFrameValue = ReadPixelOffsetFromScrollRect(isHorizontal);
                    if (time > 0 && Mathf.Abs(lastFrameValue - startPixelOffset) > 1f)
                        // Recalculate start position if interrupted, but for simplicity we can just break.
                        // A more advanced implementation could adjust the animation from the new start point.
                        yield break;

                    time += Time.unscaledDeltaTime;

                    var newPixelOffset = Mathf.LerpUnclamped(
                        startPixelOffset,
                        targetPixelOffset,
                        curve(time, duration)
                    );

                    // Writes via the normalized-position setter (see ApplyPixelOffsetToScrollRect), which in
                    // turn fires ScrollRect.onValueChanged -> OnScrollPositionChanged, keeping
                    // ScrollController.PixelOffset (and therefore RenderSliverList's build window) in sync
                    // with the animation every frame, same as the pre-pixel-tracking version relied on.
                    ApplyPixelOffsetToScrollRect(newPixelOffset, isHorizontal);

                    startPixelOffset = newPixelOffset; // Update for next frame's interruption check.
                    yield return null;
                }

                // Ensure it ends at the exact target position.
                ApplyPixelOffsetToScrollRect(targetPixelOffset, isHorizontal);
            }
            finally
            {
                scrollRect.movementType = originalMovementType;
            }
        }
    }
}
