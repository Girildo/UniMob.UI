#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNIMOB_UI_FORCE_DIAGNOSTICS
#define UNIMOB_UI_DIAGNOSTICS
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics;
using UniMob.UI;
using UniMob.UI.Diagnostics;
using UniMob.UI.Internal.Views;
using UniMob.UI.Rendering;
using UnityEngine;

[assembly: RegisterComponentViewFactory(
    "UniMob.MultiChildLayoutView",
    typeof(RectTransform),
    typeof(MultiChildLayoutView)
)]

namespace UniMob.UI.Internal.Views
{
    public class MultiChildLayoutView : View<IMultiChildLayoutState>
    {
        private ViewMapperBase _mapper = null!;

        protected override void Awake()
        {
            base.Awake();
            _mapper = new PooledViewMapper(transform);
        }

        protected override void Render()
        {
            EditorUpdateName();

            if (State.RenderObject is not IMultiChildrenRenderObject multiChildRenderObject)
                return;

            // Hoisted, not read per iteration: an atom read inside a running atom appends a
            // dependency edge every time, and AtomBase does not dedupe them. Read in the loop, a
            // 40-child column registers 81 edges per render instead of one.
            var children = State.Children;
            var childrenLayout = multiChildRenderObject.ChildrenLayout;

            using var render = _mapper.CreateRender();

            // Render each child
            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];

                if (child is null)
                {
                    // Structural, so it stays a throw: there is no layout to continue with and no
                    // remedy to offer beyond "do not do that". It does get the path, because "some
                    // multi-child widget somewhere" was never enough to act on.
                    throw new InvalidOperationException(
                        $"Child state at position {i} is null. All children must have a valid state; "
                            + $"use SizedBox.Shrink() if necessary.\n  at      {WidgetPath.From(State)}"
                    );
                }

                var layoutData = childrenLayout[i];

                // Paint is the last place this fault is decidable, and the only one: infinity is legal
                // in transit right up until it reaches a RectTransform. Repaired here to zero on the
                // offending axis, in every build, like everywhere else -- the old fallback to
                // rect.size was last frame's Unity state, so it was neither deterministic nor
                // reproducible, and it was written into a local nothing downstream ever read.
                var nonFiniteAxes = layoutData.Size.NonFiniteAxes();
                var size = layoutData.Size.ZeroOn(nonFiniteAxes);

                NoteNonFiniteChild(i, child, nonFiniteAxes);

                var childView = render.RenderItem(child);
                var rt = childView.rectTransform;

                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(0, 1);

                var pivotOffset = new Vector2(size.x * rt.pivot.x, -size.y * (1.0f - rt.pivot.y));

                rt.sizeDelta = size;
                rt.anchoredPosition =
                    new Vector2(layoutData.Position.x, -layoutData.Position.y) + pivotOffset;

#if UNIMOB_UI_DIAGNOSTICS
                // Level-triggered: drawn for exactly as long as the fault is happening. The report
                // that says it *started* was already made, once, by whoever noticed it.
                if (nonFiniteAxes != LayoutAxes.None || layoutData.Issue.HasValue)
                {
                    _warnings.Paint(rt);
                }
#endif
                // Synchronize the hierarchy order with the layout order, so the Editor hierarchy is a faithful representation of the layout.
                // This matters for paint order in ZStack especially: Unity paints from the first child to the last,
                // so a layout that draws a child on top of another must have it later in the hierarchy.
                if (rt.GetSiblingIndex() != i)
                {
                    rt.SetSiblingIndex(i);
                }
            }

#if UNIMOB_UI_DIAGNOSTICS
            _warnings.HideUnused();
#endif
        }

        /// <summary>
        ///     Reports a child whose size cannot reach a RectTransform, once per child until it stops.
        /// </summary>
        /// <remarks>
        ///     Its own dedup rather than a render object's latch: that one is scoped to a layout pass,
        ///     and this fires from paint, which can run many times without a pass in between. Called for
        ///     every child on every frame, with <see cref="LayoutAxes.None"/> meaning "healthy", so the
        ///     entry re-arms the moment the fault clears.
        /// </remarks>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("UNIMOB_UI_FORCE_DIAGNOSTICS")]
        private void NoteNonFiniteChild(int index, IState child, LayoutAxes axes)
        {
#if UNIMOB_UI_DIAGNOSTICS
            if (axes == LayoutAxes.None)
            {
                _reportedNonFinite.Remove(index);
                return;
            }

            if (!_reportedNonFinite.Add(index))
            {
                return;
            }

            UniMobDiagnostics.Report(
                new LayoutIssue(
                    LayoutIssueCode.NonFiniteChildSize,
                    State,
                    axes,
                    BoundItBeforeItIsPainted,
                    child,
                    size: child.RenderObject?.PeekSize()
                )
            );
#endif
        }

        /// <summary>
        ///     Names this object after the widget it paints, for the Editor hierarchy.
        /// </summary>
        /// <remarks>
        ///     Rebuilt only when the bound widget type changes. Render runs on every layout pass, so
        ///     naming unconditionally costs two string allocations per view per frame.
        /// </remarks>
        [Conditional("UNITY_EDITOR")]
        private void EditorUpdateName()
        {
#if UNITY_EDITOR
            var widgetType = State.RawWidget.GetType();
            if (ReferenceEquals(widgetType, _namedFor))
            {
                return;
            }

            _namedFor = widgetType;
            this.name = EditorViewName.For(widgetType, "[MultiChildLayoutView]");
#endif
        }

#if UNITY_EDITOR
        private Type? _namedFor;
#endif

#if UNIMOB_UI_DIAGNOSTICS
        private const string BoundItBeforeItIsPainted =
            "An infinite size is legal in transit and fatal at a RectTransform. Bound this child "
            + "(Expanded, SizedBox, or a fixed-size ancestor) so it materialises before it is painted.";

        private readonly HashSet<int> _reportedNonFinite = new HashSet<int>();

        private readonly LayoutWarningOverlay _warnings = new LayoutWarningOverlay();
#endif
    }
}
