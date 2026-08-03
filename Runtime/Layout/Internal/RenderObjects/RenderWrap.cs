#nullable enable
using System.Collections.Generic;
using UniMob.UI.Layout.Internal.Views;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
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
            public float MainSize;         // Includes the standard 'Spacing' between items
            public float ChildrenMainSize; // Raw size of children without spacing
            public float CrossSize;
        }
        private readonly List<RunMetrics> _runs = new();

        public RenderWrap(IWrapState state) : base(state)
        {
            _state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            ChildrenLayoutBuffer.Clear();
            _runs.Clear();

            var childConstraints = constraints.Loosen(); 

            var currentMain = 0f;
            var childrenMainSum = 0f;
            var maxRunCross = 0f;
            var maxOverallMain = 0f;

            var mainAxisConstraint = constraints.MaxAlongAxis(Direction);

            var currentRun = new RunMetrics { StartIndex = 0 };

            for (var i = 0; i < _state.Children.Length; i++)
            {
                var child = _state.Children[i];
                var childSize = LayoutChild(child, childConstraints);
                
                ChildrenLayoutBuffer.Add(new LayoutInfo { Size = childSize });

                var childMain = Direction == Axis.Horizontal ? childSize.x : childSize.y;
                var childCross = Direction == Axis.Horizontal ? childSize.y : childSize.x;

                if (currentRun.Count > 0 && currentMain + childMain > mainAxisConstraint)
                {
                    currentRun.MainSize = currentMain - Spacing;
                    currentRun.ChildrenMainSize = childrenMainSum;
                    currentRun.CrossSize = maxRunCross;
                    _runs.Add(currentRun);

                    maxOverallMain = Mathf.Max(maxOverallMain, currentRun.MainSize);

                    currentMain = 0;
                    childrenMainSum = 0;
                    maxRunCross = 0;
                    currentRun = new RunMetrics { StartIndex = i };
                }

                currentMain += childMain + Spacing;
                childrenMainSum += childMain;
                maxRunCross = Mathf.Max(maxRunCross, childCross);
                currentRun.Count++;
            }

            if (currentRun.Count > 0)
            {
                currentRun.MainSize = currentMain - Spacing;
                currentRun.ChildrenMainSize = childrenMainSum;
                currentRun.CrossSize = maxRunCross;
                _runs.Add(currentRun);
                maxOverallMain = Mathf.Max(maxOverallMain, currentRun.MainSize);
            }

            var totalCross = 0f;
            for (var r = 0; r < _runs.Count; r++)
            {
                totalCross += _runs[r].CrossSize + (r > 0 ? RunSpacing : 0);
            }

            var finalWidth = Direction == Axis.Horizontal ? maxOverallMain : totalCross;
            var finalHeight = Direction == Axis.Horizontal ? totalCross : maxOverallMain;

            return constraints.Constrain(new Vector2(finalWidth, finalHeight));
        }

        protected override void PerformPositioning()
        {
            var wrapMainSize = Direction == Axis.Horizontal ? Size.x : Size.y;
            var wrapCrossSize = Direction == Axis.Horizontal ? Size.y : Size.x;

            // 1. Calculate Run Alignment (Cross Axis Distribution of the lines)
            var totalRunsCrossSize = 0f;
            foreach (var r in _runs) totalRunsCrossSize += r.CrossSize;
            
            var freeRunSpace = Mathf.Max(0, wrapCrossSize - totalRunsCrossSize);
            var currentCross = 0f;
            var runStep = RunSpacing;

            if (_runs.Count > 0)
            {
                switch (RunAlignment)
                {
                    case MainAxisAlignment.Start: break;
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
                    
                    var childMain = Direction == Axis.Horizontal ? layoutData.Size.x : layoutData.Size.y;
                    var childCross = Direction == Axis.Horizontal ? layoutData.Size.y : layoutData.Size.x;

                    // Calculate CrossAxisAlignment (Vertical alignment within the line's max height)
                    var childCrossOffset = 0f;
                    var freeChildCross = Mathf.Max(0, run.CrossSize - childCross);

                    switch (CrossAxisAlignment)
                    {
                        case CrossAxisAlignment.Start: break;
                        case CrossAxisAlignment.End: childCrossOffset = freeChildCross; break;
                        case CrossAxisAlignment.Center: childCrossOffset = freeChildCross / 2f; break;
                        case CrossAxisAlignment.Stretch: 
                            childCross = run.CrossSize;
                            if (Direction == Axis.Horizontal) layoutData.Size.y = childCross;
                            else layoutData.Size.x = childCross;
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

        protected override float ComputeIntrinsicWidth(float height)
        {
            if (Direction == Axis.Horizontal)
            {
                var totalWidth = 0f;
                foreach (var child in _state.Children)
                {
                    totalWidth += child.RenderObject.GetIntrinsicWidth(height) + Spacing;
                }
                return Mathf.Max(0, totalWidth - Spacing);
            }
            else
            {
                var constraints = new LayoutConstraints(0, 0, 0, height);
                return PerformSizing(constraints).x;
            }
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
            else
            {
                var constraints = new LayoutConstraints(0, width, 0, float.PositiveInfinity);
                return PerformSizing(constraints).y;
            }
        }
    }
}