using System.Collections.Generic;
using UniMob.UI.Internal.Views;
using UnityEngine;

namespace UniMob.UI.Rendering
{
    public class RenderWrap : MultiChildRenderObject
    {
        private readonly IWrapState _state;

        // Immutable properties pulled directly from state
        private Axis Direction => _state.Direction;
        private float Spacing => _state.Spacing;
        private float RunSpacing => _state.RunSpacing;
        private MainAxisAlignment Alignment => _state.Alignment;
        private CrossAxisAlignment CrossAxisAlignment => _state.CrossAxisAlignment;
        private MainAxisAlignment RunAlignment => _state.RunAlignment;

        private class RunMetrics
        {
            public int StartIndex;
            public int Count;
            public float MainSize; // Includes the standard 'Spacing' between items
            public float ChildrenMainSize; // Raw size of children without spacing
            public float CrossSize;
        }

        private readonly List<RunMetrics> _runs = new();
        private readonly List<Vector2> _childSizes = new();

        // Scratch for the dry pass, deliberately not the buffers above: a measurement that shared
        // them would answer correctly and still leave the live layout holding its own results.
        private readonly List<RunMetrics> _dryRuns = new();
        private readonly List<Vector2> _dryChildSizes = new();

        public RenderWrap(IWrapState state)
            : base(state)
        {
            _state = state;
        }

        /// <summary>
        ///     Greedily packs <paramref name="childSizes"/> into runs against
        ///     <paramref name="mainAxisLimit"/>, appending each one to <paramref name="runs"/>, and
        ///     returns the extent they occupy as <c>(main, cross)</c>.
        /// </summary>
        /// <remarks>
        ///     Arithmetic over the sizes it is handed, and nothing else. Whether those came from laying
        ///     the children out or from asking them their intrinsics is the caller's business, which is
        ///     what lets a sizing pass and a dry measurement share one definition of where runs break.
        /// </remarks>
        private Vector2 PackRuns(
            IReadOnlyList<Vector2> childSizes,
            float mainAxisLimit,
            List<RunMetrics> runs
        )
        {
            var isHorizontal = Direction == Axis.Horizontal;
            var spacing = Spacing;

            var currentMain = 0f;
            var childrenMainSum = 0f;
            var maxRunCross = 0f;
            var maxOverallMain = 0f;

            var currentRun = new RunMetrics { StartIndex = 0 };

            for (var i = 0; i < childSizes.Count; i++)
            {
                var childSize = childSizes[i];
                var childMain = isHorizontal ? childSize.x : childSize.y;
                var childCross = isHorizontal ? childSize.y : childSize.x;

                if (currentRun.Count > 0 && currentMain + childMain > mainAxisLimit)
                {
                    currentRun.MainSize = currentMain - spacing;
                    currentRun.ChildrenMainSize = childrenMainSum;
                    currentRun.CrossSize = maxRunCross;
                    runs.Add(currentRun);

                    maxOverallMain = Mathf.Max(maxOverallMain, currentRun.MainSize);

                    currentMain = 0;
                    childrenMainSum = 0;
                    maxRunCross = 0;
                    currentRun = new RunMetrics { StartIndex = i };
                }

                currentMain += childMain + spacing;
                childrenMainSum += childMain;
                maxRunCross = Mathf.Max(maxRunCross, childCross);
                currentRun.Count++;
            }

            if (currentRun.Count > 0)
            {
                currentRun.MainSize = currentMain - spacing;
                currentRun.ChildrenMainSize = childrenMainSum;
                currentRun.CrossSize = maxRunCross;
                runs.Add(currentRun);
                maxOverallMain = Mathf.Max(maxOverallMain, currentRun.MainSize);
            }

            var totalCross = 0f;
            for (var r = 0; r < runs.Count; r++)
            {
                totalCross += runs[r].CrossSize + (r > 0 ? RunSpacing : 0);
            }

            return new Vector2(maxOverallMain, totalCross);
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            ChildrenLayoutBuffer.Clear();
            _runs.Clear();
            _childSizes.Clear();

            var childConstraints = constraints.Loosen();
            var children = _state.Children;

            for (var i = 0; i < children.Length; i++)
            {
                _childSizes.Add(LayoutChild(children[i], childConstraints));
            }

            var extent = PackRuns(_childSizes, constraints.MaxAlongAxis(Direction), _runs);

            for (var i = 0; i < _childSizes.Count; i++)
            {
                ChildrenLayoutBuffer.Add(new LayoutInfo { Size = _childSizes[i] });
            }

            var desired =
                Direction == Axis.Horizontal
                    ? new Vector2(extent.x, extent.y)
                    : new Vector2(extent.y, extent.x);

            // Both axes, and both are reachable. A run always accepts its first child, so one child
            // wider than the whole wrap overflows the main axis; and nothing bounds the number of
            // runs, so enough of them overflow the cross axis.
            ReportContentOverflow(constraints, desired, MakeRoomForTheRuns);

            return constraints.Constrain(desired);
        }

        protected override void PerformPositioning(Vector2 size)
        {
            var wrapMainSize = Direction == Axis.Horizontal ? size.x : size.y;
            var wrapCrossSize = Direction == Axis.Horizontal ? size.y : size.x;

            // 1. Calculate Run Alignment (Cross Axis Distribution of the lines)
            var totalRunsCrossSize = 0f;
            foreach (var r in _runs)
                totalRunsCrossSize += r.CrossSize;

            var freeRunSpace = Mathf.Max(0, wrapCrossSize - totalRunsCrossSize);
            var currentCross = 0f;
            var runStep = RunSpacing;

            if (_runs.Count > 0)
            {
                switch (RunAlignment)
                {
                    case MainAxisAlignment.Start:
                        break;
                    case MainAxisAlignment.End:
                        currentCross = freeRunSpace - (_runs.Count - 1) * RunSpacing;
                        break;
                    case MainAxisAlignment.Center:
                        currentCross = (freeRunSpace - (_runs.Count - 1) * RunSpacing) / 2f;
                        break;
                    case MainAxisAlignment.SpaceBetween:
                        runStep = _runs.Count > 1 ? freeRunSpace / (_runs.Count - 1) : 0;
                        break;
                    case MainAxisAlignment.SpaceAround:
                        runStep = freeRunSpace / _runs.Count;
                        currentCross = runStep / 2f;
                        break;
                    case MainAxisAlignment.SpaceEvenly:
                        runStep = freeRunSpace / (_runs.Count + 1);
                        currentCross = runStep;
                        break;
                }
            }

            // 2. Position items within each run
            for (var r = 0; r < _runs.Count; r++)
            {
                var run = _runs[r];

                // Calculate Main Alignment (Main Axis Distribution of items on this specific line)
                var freeMainSpace = Mathf.Max(0, wrapMainSize - run.ChildrenMainSize);
                var currentMain = 0f;
                var mainStep = Spacing;

                if (run.Count > 0)
                {
                    switch (Alignment)
                    {
                        case MainAxisAlignment.Start:
                            break;
                        case MainAxisAlignment.End:
                            currentMain = freeMainSpace - (run.Count - 1) * Spacing;
                            break;
                        case MainAxisAlignment.Center:
                            currentMain = (freeMainSpace - (run.Count - 1) * Spacing) / 2f;
                            break;
                        case MainAxisAlignment.SpaceBetween:
                            mainStep = run.Count > 1 ? freeMainSpace / (run.Count - 1) : 0;
                            break;
                        case MainAxisAlignment.SpaceAround:
                            mainStep = freeMainSpace / run.Count;
                            currentMain = mainStep / 2f;
                            break;
                        case MainAxisAlignment.SpaceEvenly:
                            mainStep = freeMainSpace / (run.Count + 1);
                            currentMain = mainStep;
                            break;
                    }
                }

                for (var i = 0; i < run.Count; i++)
                {
                    var childIndex = run.StartIndex + i;
                    var layoutData = ChildrenLayoutBuffer[childIndex];

                    var childMain =
                        Direction == Axis.Horizontal ? layoutData.Size.x : layoutData.Size.y;
                    var childCross =
                        Direction == Axis.Horizontal ? layoutData.Size.y : layoutData.Size.x;

                    // Calculate CrossAxisAlignment (Vertical alignment within the line's max height)
                    var childCrossOffset = 0f;
                    var freeChildCross = Mathf.Max(0, run.CrossSize - childCross);

                    switch (CrossAxisAlignment)
                    {
                        case CrossAxisAlignment.Start:
                            break;
                        case CrossAxisAlignment.End:
                            childCrossOffset = freeChildCross;
                            break;
                        case CrossAxisAlignment.Center:
                            childCrossOffset = freeChildCross / 2f;
                            break;
                        case CrossAxisAlignment.Stretch:
                            childCross = run.CrossSize;
                            if (Direction == Axis.Horizontal)
                                layoutData.Size.y = childCross;
                            else
                                layoutData.Size.x = childCross;
                            break;
                    }

                    var finalCross = currentCross + childCrossOffset;

                    var xPos = Direction == Axis.Horizontal ? currentMain : finalCross;
                    var yPos = Direction == Axis.Horizontal ? finalCross : currentMain;

                    layoutData.Position = new Vector2(xPos, yPos);
                    ChildrenLayoutBuffer[childIndex] = layoutData;

                    currentMain += childMain + mainStep;
                }

                currentCross += run.CrossSize + runStep;
            }
        }

        /// <summary>
        ///     The cross-axis extent the runs need when the children are packed against
        ///     <paramref name="mainAxisLimit"/>, measured through their intrinsics rather than by laying
        ///     them out.
        /// </summary>
        /// <remarks>
        ///     The cross axis has no closed form: how many runs there are, and how thick each one is,
        ///     both fall out of where the runs happen to break, so it has to be packed to be known.
        ///     <para>
        ///         Packing it through intrinsics is what keeps the answer free of side effects. Reaching
        ///         for <see cref="RenderObject.LayoutChild"/> here would commit these speculative
        ///         constraints onto real children, leaving them measured against a box nobody is going
        ///         to draw them in until something lays them out again.
        ///     </para>
        /// </remarks>
        private float DryCrossExtent(float mainAxisLimit)
        {
            _dryRuns.Clear();
            _dryChildSizes.Clear();

            foreach (var child in _state.Children)
            {
                var render = child.RenderObject;

                // Width first, then the height that width implies: a child whose height depends on its
                // width (any wrapping text) answers for the shape it would actually take.
                var childWidth = render.GetIntrinsicWidth(float.PositiveInfinity);
                _dryChildSizes.Add(new Vector2(childWidth, render.GetIntrinsicHeight(childWidth)));
            }

            return PackRuns(_dryChildSizes, mainAxisLimit, _dryRuns).y;
        }

        protected override float ComputeIntrinsicWidth(float height)
        {
            // Along the main axis the runs are irrelevant: one unwrapped line is the widest this can
            // want, whatever the height does to where it would break.
            if (Direction == Axis.Horizontal)
            {
                var totalWidth = 0f;
                foreach (var child in _state.Children)
                {
                    totalWidth += child.RenderObject.GetIntrinsicWidth(height) + Spacing;
                }
                return Mathf.Max(0, totalWidth - Spacing);
            }

            return DryCrossExtent(mainAxisLimit: height);
        }

        protected override float ComputeIntrinsicHeight(float width)
        {
            if (Direction == Axis.Vertical)
            {
                var totalHeight = 0f;
                foreach (var child in _state.Children)
                {
                    totalHeight += child.RenderObject.GetIntrinsicHeight(width) + Spacing;
                }
                return Mathf.Max(0, totalHeight - Spacing);
            }

            return DryCrossExtent(mainAxisLimit: width);
        }

        private const string MakeRoomForTheRuns =
            "The runs need more room than this box allows. Give the Wrap a larger box, let it "
            + "scroll, or reduce the children's size along the run direction.";
    }
}
