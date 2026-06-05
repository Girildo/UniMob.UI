using System.Collections.Generic;
using UniMob.UI.Layout.Internal.Views;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    public interface IFlexContainerState : IMultiChildLayoutState
    {
        CrossAxisAlignment CrossAxisAlignment { get; }
        MainAxisAlignment MainAxisAlignment { get; }
        AxisSize MainAxisSize { get; }
        float Spacing { get; }

    }

    internal class RenderFlex : RenderObject, IMultiChildrenRenderObject
    {
        private readonly IFlexContainerState _state;
        private readonly Axis _axis;
        private float _unconstrainedMainAxisSize;
        private readonly List<LayoutData> _childrenLayout = new();

        public IReadOnlyList<LayoutData> ChildrenLayout => _childrenLayout;

        public RenderFlex(IFlexContainerState state, Axis axis) : base(state.StateLifetime)
        {
            _state = state;
            _axis = axis;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            _childrenLayout.Clear();
            var mainAxisTotalSize = 0f;
            var crossAxisMaxSize = 0f;
            var totalFlexFactor = 0;

            var isHorizontal = _axis == Axis.Horizontal;
            var maxMainAxis = isHorizontal ? constraints.MaxWidth : constraints.MaxHeight;
            var maxCrossAxis = isHorizontal ? constraints.MaxHeight : constraints.MaxWidth;

            var nonFlexChildrenIndices = new List<int>();
            var flexChildrenData = new List<(int index, int flex)>();

            // --- First Pass: Identify flex vs. non-flex children ---
            var childCount = _state.Children.Length;
            for (var i = 0; i < childCount; i++)
            {
                var childState = _state.Children[i];

                _childrenLayout.Add(new LayoutData()); // Add a placeholder

                var isFlexible = false;
                var flexFactor = 1;

                // Check if the child is an Expanded (flex) widget
                // -- remark: we use InnerViewState because Expanded might be composed inside other Hoc widgets
                if (childState.InnerViewState is ExpandedState flex)
                {
                    isFlexible = true;
                    flexFactor = flex.Flex;
                }

                // Legacy widgets are considered flexible if they have infinite constraints in the main axis
                if (childState.RenderObject is RenderLegacy)
                {
                    var legacySize = childState.Size;
                    if (isHorizontal ? float.IsInfinity(legacySize.MaxWidth) : float.IsInfinity(legacySize.MaxHeight))
                    {
                        isFlexible = true;
                    }
                }

                if (isFlexible)
                {
                    totalFlexFactor += flexFactor;
                    flexChildrenData.Add((i, flexFactor));
                }
                else
                {
                    nonFlexChildrenIndices.Add(i);
                }
            }

            // --- Second Pass: Measure non-flexible children ---
            var nonFlexConstraints = isHorizontal
                ? new LayoutConstraints(0, 0, float.PositiveInfinity, maxCrossAxis)
                : new LayoutConstraints(0, 0, maxCrossAxis, float.PositiveInfinity);
            var shouldStretchCrossAxis = _state.CrossAxisAlignment == CrossAxisAlignment.Stretch;

            foreach (var i in nonFlexChildrenIndices)
            {
                var childConstraints = nonFlexConstraints;
                if (shouldStretchCrossAxis)
                {
                    childConstraints = isHorizontal
                        ? childConstraints.Tighten(height: maxCrossAxis)
                        : childConstraints.Tighten(width: maxCrossAxis);
                }

                var childSize = LayoutChild(_state.Children[i], childConstraints);
                _childrenLayout[i] = new LayoutData { Size = childSize };
                mainAxisTotalSize += isHorizontal ? childSize.x : childSize.y;
                crossAxisMaxSize = Mathf.Max(crossAxisMaxSize, isHorizontal ? childSize.y : childSize.x);
            }

            // --- Calculate total fixed spacing ---
            var fixedSpacing = _state.Spacing;
            var totalSpacing = childCount > 0 ? Mathf.Max(0, childCount - 1) * fixedSpacing : 0;

            // --- Third Pass: Layout flexible (Expanded or Stretched) children ---
            var freeSpace = maxMainAxis - mainAxisTotalSize - totalSpacing;

            if (freeSpace < 0)
            {
                Debug.LogWarning("No free space available for flexible children. They will be sized to zero.");
                freeSpace = 0;
            }

            if (totalFlexFactor > 0)
            {
                foreach (var (i, flex) in flexChildrenData)
                {
                    var flexSpace = freeSpace * (flex / (float) totalFlexFactor);

                    LayoutConstraints flexConstraints;
                    if (isHorizontal)
                    {
                        // If stretching, the cross-axis (height) is tight. Otherwise, it's loose (0 to max).
                        flexConstraints = LayoutConstraints.TightFor(width: flexSpace)
                            .Tighten(height: shouldStretchCrossAxis ? maxCrossAxis : null);
                    }
                    else // isVertical
                    {
                        // If stretching, the cross-axis (width) is tight. Otherwise, it's loose (0 to max).
                        flexConstraints = LayoutConstraints.TightFor(height: flexSpace)
                            .Tighten(width: shouldStretchCrossAxis ? maxCrossAxis : null);
                    }

                    var childSize = LayoutChild(_state.Children[i], flexConstraints);
                    _childrenLayout[i] = new LayoutData { Size = childSize };
                    crossAxisMaxSize = Mathf.Max(crossAxisMaxSize, isHorizontal ? childSize.y : childSize.x);
                }
            }

            // --- Final Size Calculation ---
            var totalUsedMainAxisSize = 0f;
            foreach (var layoutData in _childrenLayout)
            {
                totalUsedMainAxisSize += isHorizontal ? layoutData.Size.x : layoutData.Size.y;
            }
            _unconstrainedMainAxisSize = totalUsedMainAxisSize + totalSpacing;

            var finalMainAxisSize = _state.MainAxisSize == AxisSize.Max ? maxMainAxis : _unconstrainedMainAxisSize;

            var finalSize = isHorizontal
                ? new Vector2(finalMainAxisSize, crossAxisMaxSize)
                : new Vector2(crossAxisMaxSize, finalMainAxisSize);

            return constraints.Constrain(finalSize);

            
        }

        protected override void PerformPositioning()
        {
            var isHorizontal = _axis == Axis.Horizontal;
            var mainAxisSize = isHorizontal ? Size.x : Size.y;
            var freeSpace = (isHorizontal ? Size.x : Size.y) - _unconstrainedMainAxisSize;
            float mainAxisPos = 0;
            float alignmentSpacing = 0;
            var childCount = _childrenLayout.Count;
            if (freeSpace > 0 && !float.IsInfinity(mainAxisSize))
            {
                // --- MAIN AXIS ALIGNMENT ---
                switch (_state.MainAxisAlignment)
                {
                    case MainAxisAlignment.Start:
                        mainAxisPos = 0;
                        break;
                    case MainAxisAlignment.Center:
                        mainAxisPos = freeSpace / 2f;
                        break;
                    case MainAxisAlignment.End:
                        mainAxisPos = freeSpace;
                        break;
                    case MainAxisAlignment.SpaceAround:
                        alignmentSpacing = childCount > 0 ? freeSpace / childCount : 0;
                        mainAxisPos = alignmentSpacing / 2f;
                        break;
                    case MainAxisAlignment.SpaceBetween:
                        alignmentSpacing = childCount > 1 ? freeSpace / (childCount - 1) : 0;
                        break;
                    case MainAxisAlignment.SpaceEvenly:
                        alignmentSpacing = childCount > 0 ? freeSpace / (childCount + 1) : 0;
                        mainAxisPos = alignmentSpacing;
                        break;
                }
            }

            var fixedSpacing = _state.Spacing;

            for (var i = 0; i < _childrenLayout.Count; i++)
            {
                var layout = _childrenLayout[i];

                // --- CROSS AXIS ALIGNMENT ---
                var crossAxisSize = isHorizontal ? this.Size.y : this.Size.x;
                var childCrossAxisSize = isHorizontal ? layout.Size.y : layout.Size.x;


                var crossAxisPos = _state.CrossAxisAlignment switch
                {
                    CrossAxisAlignment.Center => (crossAxisSize - childCrossAxisSize) / 2f,
                    CrossAxisAlignment.End => crossAxisSize - childCrossAxisSize,
                    _ => 0, // Start and Stretch align to 0
                };
                var newLayoutData = _childrenLayout[i];
                newLayoutData.CornerPosition = isHorizontal
                    ? new Vector2(mainAxisPos, crossAxisPos)
                    : new Vector2(crossAxisPos, mainAxisPos);
                _childrenLayout[i] = newLayoutData;
                mainAxisPos += (isHorizontal ? layout.Size.x : layout.Size.y) + alignmentSpacing;

                if (i < _childrenLayout.Count - 1)
                {
                    mainAxisPos += fixedSpacing;
                }
            }
        }

        // --- GENERIC INTRINSIC SIZING ---
        protected override float ComputeIntrinsicHeight(float width)
        {
            if (_axis == Axis.Vertical) // Sum of heights (for a Column)
            {
                float totalHeight = 0;
                foreach (var child in _state.Children)
                {
                    totalHeight += GetChildIntrinsicHeight(child, width);
                }

                if (_state.Children.Length > 0)
                {
                    totalHeight += Mathf.Max(0, _state.Children.Length - 1) * _state.Spacing;
                }
                return totalHeight;
            }
            else // Max of heights (for a Row)
            {
                float maxHeight = 0;
                // Cannot know width for each child, so we pass infinite. This is a limitation.
                foreach (var child in _state.Children)
                {
                    maxHeight = Mathf.Max(maxHeight, GetChildIntrinsicHeight(child, float.PositiveInfinity));
                }

                return maxHeight;
            }
        }

        protected override float ComputeIntrinsicWidth(float height)
        {
            if (_axis == Axis.Horizontal) // Sum of widths (for a Row)
            {
                float totalWidth = 0;
                foreach (var child in _state.Children)
                {
                    totalWidth += GetChildIntrinsicWidth(child, height);
                }

                // ADDED: Account for spacing in intrinsic width for Horizontal axis
                if (_state.Children.Length > 0)
                {
                    totalWidth += Mathf.Max(0, _state.Children.Length - 1) * _state.Spacing;
                }

                return totalWidth;
            }
            else // Max of widths (for a Column)
            {
                float maxWidth = 0;
                foreach (var child in _state.Children)
                {
                    maxWidth = Mathf.Max(maxWidth, GetChildIntrinsicWidth(child, float.PositiveInfinity));
                }

                return maxWidth;
            }
        }

        private float GetChildIntrinsicWidth(IState child, float height)
        {
            return child.RenderObject.GetIntrinsicWidth(height);
        }

        private float GetChildIntrinsicHeight(IState child, float width)
        {
            return child.RenderObject.GetIntrinsicHeight(width);
        }

    }
}