using System.Collections.Generic;
using UniMob.UI.Diagnostics;
using UniMob.UI.Internal.Views;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Rendering
{
    public interface IFlexContainerState : IMultiChildLayoutState
    {
        CrossAxisAlignment CrossAxisAlignment { get; }
        MainAxisAlignment MainAxisAlignment { get; }
        AxisSize MainAxisSize { get; }
        float Spacing { get; }
    }

    public class RenderFlex : MultiChildRenderObject
    {
        private readonly IFlexContainerState _state;
        private readonly Axis _axis;
        private float _unconstrainedMainAxisSize;
        private readonly List<int> _nonFlexChildrenIndices = new();
        private readonly List<(int index, int flex, FlexFit fit)> _flexChildrenData = new();

        public RenderFlex(IFlexContainerState state, Axis axis)
            : base(state)
        {
            _state = state;
            _axis = axis;
        }

        private LayoutAxes MainAxis =>
            _axis == Axis.Horizontal ? LayoutAxes.Horizontal : LayoutAxes.Vertical;

        private LayoutAxes CrossAxis =>
            _axis == Axis.Horizontal ? LayoutAxes.Vertical : LayoutAxes.Horizontal;

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            ChildrenLayoutBuffer.Clear();
            _nonFlexChildrenIndices.Clear();
            _flexChildrenData.Clear();
            var mainAxisTotalSize = 0f;
            var crossAxisMaxSize = 0f;
            var totalFlexFactor = 0;

            var isHorizontal = _axis == Axis.Horizontal;
            var maxMainAxis = isHorizontal ? constraints.MaxWidth : constraints.MaxHeight;
            var maxCrossAxis = isHorizontal ? constraints.MaxHeight : constraints.MaxWidth;

            // --- First Pass: Identify flex vs. non-flex children ---
            var childCount = _state.Children.Length;
            for (var i = 0; i < childCount; i++)
            {
                var childState = _state.Children[i];

                ChildrenLayoutBuffer.Add(new LayoutInfo()); // Add a placeholder

                var isFlexible = TryGetFlex(
                    childState,
                    isHorizontal,
                    out var flexFactor,
                    out var fit
                );

                if (isFlexible)
                {
                    totalFlexFactor += flexFactor;
                    _flexChildrenData.Add((i, flexFactor, fit));
                }
                else
                {
                    _nonFlexChildrenIndices.Add(i);
                }
            }

            // --- Second Pass: Measure non-flexible children ---
            var nonFlexConstraints = isHorizontal
                ? new LayoutConstraints(0, 0, float.PositiveInfinity, maxCrossAxis)
                : new LayoutConstraints(0, 0, maxCrossAxis, float.PositiveInfinity);
            // Stretch means "be exactly as big as the cross axis", which has no answer when that axis
            // is unbounded -- Tighten() would pin every child to a tight infinity, and each of them
            // would then correctly report infinity, which the parent flex zeroes. A row measured for
            // its natural height (any non-flex child of a Column) is exactly that case, so stretching
            // is dropped there and the children hug instead. It is reported rather than merely
            // dropped: the children that go on to answer infinity are all innocent, so without this
            // the console names every one of them and never the row that asked for the impossible.
            var wantsStretch = _state.CrossAxisAlignment == CrossAxisAlignment.Stretch;
            var shouldStretchCrossAxis = wantsStretch && !float.IsInfinity(maxCrossAxis);

            if (wantsStretch && !shouldStretchCrossAxis)
            {
                ReportUnboundedConstraint(CrossAxis, constraints, BoundTheCrossAxisOrDoNotStretch);
            }

            foreach (var i in _nonFlexChildrenIndices)
            {
                var childConstraints = nonFlexConstraints;
                if (shouldStretchCrossAxis)
                {
                    childConstraints = isHorizontal
                        ? childConstraints.Tighten(height: maxCrossAxis)
                        : childConstraints.Tighten(width: maxCrossAxis);
                }

                var childSize = LayoutChild(_state.Children[i], childConstraints);
                var childIsNonFinite = isHorizontal
                    ? float.IsInfinity(childSize.x)
                    : float.IsInfinity(childSize.y);

                if (childIsNonFinite)
                {
                    childSize = childSize.ZeroOn(MainAxis);
                }

                // Written before the report, which marks this entry for the in-scene overlay.
                ChildrenLayoutBuffer[i] = new LayoutInfo { Size = childSize };

                if (childIsNonFinite)
                {
                    ReportNonFiniteChildSize(i, MainAxis, WrapInFlexible);
                }

                mainAxisTotalSize += isHorizontal ? childSize.x : childSize.y;
                crossAxisMaxSize = Mathf.Max(
                    crossAxisMaxSize,
                    isHorizontal ? childSize.y : childSize.x
                );
            }

            // --- Calculate total fixed spacing ---
            var fixedSpacing = _state.Spacing;
            var totalSpacing = childCount > 0 ? Mathf.Max(0, childCount - 1) * fixedSpacing : 0;

            // --- Third Pass: Layout flexible (Expanded or Stretched) children ---
            var freeSpace = maxMainAxis - mainAxisTotalSize - totalSpacing;

            if (freeSpace < 0)
            {
                // Flexible children cannot be given negative space, so the overflow is absorbed here
                // whether or not anything is said about it. The tolerance band that decides whether
                // to say anything belongs to the report, not to this.
                ReportOverflow(MainAxis, -freeSpace, LargestNonFlexChild(), MakeRoomOrScroll);

                freeSpace = 0;
            }

            if (totalFlexFactor > 0)
            {
                if (float.IsInfinity(maxMainAxis))
                {
                    ReportUnboundedConstraint(MainAxis, constraints, BoundTheMainAxis);
                }

                foreach (var (i, flex, fit) in _flexChildrenData)
                {
                    var flexSpace = freeSpace * (flex / (float)totalFlexFactor);

                    // Fit decides the MAIN axis only: tight pins the child to its share, loose lets it
                    // take less. The cross axis is the flex's own box either way, so it is passed on
                    // whatever the fit -- a child cannot legally be laid out taller than the row it is
                    // in. Dropping it (as a bare TightFor on the main axis does) hands the child an
                    // unbounded cross axis, and anything that sizes itself from what it is given --
                    // a fill-me leaf, a scroll viewport -- then has nothing to resolve against.
                    LayoutConstraints flexConstraints;
                    if (isHorizontal)
                    {
                        flexConstraints =
                            fit == FlexFit.Tight
                                ? new LayoutConstraints(
                                    minWidth: flexSpace,
                                    minHeight: 0,
                                    maxWidth: flexSpace,
                                    maxHeight: maxCrossAxis
                                )
                                : new LayoutConstraints(
                                    minWidth: 0,
                                    minHeight: 0,
                                    maxWidth: flexSpace,
                                    maxHeight: maxCrossAxis
                                );

                        flexConstraints = flexConstraints.Tighten(
                            height: shouldStretchCrossAxis ? maxCrossAxis : null
                        );
                    }
                    else // isVertical
                    {
                        flexConstraints =
                            fit == FlexFit.Tight
                                ? new LayoutConstraints(
                                    minWidth: 0,
                                    minHeight: flexSpace,
                                    maxWidth: maxCrossAxis,
                                    maxHeight: flexSpace
                                )
                                : new LayoutConstraints(
                                    minWidth: 0,
                                    minHeight: 0,
                                    maxWidth: maxCrossAxis,
                                    maxHeight: flexSpace
                                );

                        flexConstraints = flexConstraints.Tighten(
                            width: shouldStretchCrossAxis ? maxCrossAxis : null
                        );
                    }

                    var childSize = LayoutChild(_state.Children[i], flexConstraints);
                    var childIsNonFinite = isHorizontal
                        ? float.IsInfinity(childSize.x)
                        : float.IsInfinity(childSize.y);

                    if (childIsNonFinite)
                    {
                        childSize = childSize.ZeroOn(MainAxis);
                    }

                    ChildrenLayoutBuffer[i] = new LayoutInfo { Size = childSize };

                    if (childIsNonFinite)
                    {
                        ReportNonFiniteChildSize(i, MainAxis, WrapInFlexible);
                    }

                    crossAxisMaxSize = Mathf.Max(
                        crossAxisMaxSize,
                        isHorizontal ? childSize.y : childSize.x
                    );
                }
            }

            // --- Final Size Calculation ---
            var totalUsedMainAxisSize = 0f;
            foreach (var layoutData in ChildrenLayoutBuffer)
            {
                totalUsedMainAxisSize += isHorizontal ? layoutData.Size.x : layoutData.Size.y;
            }
            _unconstrainedMainAxisSize = totalUsedMainAxisSize + totalSpacing;

            var finalMainAxisSize =
                _state.MainAxisSize == AxisSize.Max ? maxMainAxis : _unconstrainedMainAxisSize;

            var finalSize = isHorizontal
                ? new Vector2(finalMainAxisSize, crossAxisMaxSize)
                : new Vector2(crossAxisMaxSize, finalMainAxisSize);

            return constraints.Constrain(finalSize);
        }

        protected override void PerformPositioning(Vector2 size)
        {
            var isHorizontal = _axis == Axis.Horizontal;
            var mainAxisSize = isHorizontal ? size.x : size.y;
            var freeSpace = (isHorizontal ? size.x : size.y) - _unconstrainedMainAxisSize;
            float mainAxisPos = 0;
            float alignmentSpacing = 0;
            var childCount = ChildrenLayoutBuffer.Count;
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

            for (var i = 0; i < ChildrenLayoutBuffer.Count; i++)
            {
                var layout = ChildrenLayoutBuffer[i];

                // --- CROSS AXIS ALIGNMENT ---
                var crossAxisSize = isHorizontal ? size.y : size.x;
                var childCrossAxisSize = isHorizontal ? layout.Size.y : layout.Size.x;

                var crossAxisPos = _state.CrossAxisAlignment switch
                {
                    CrossAxisAlignment.Center => (crossAxisSize - childCrossAxisSize) / 2f,
                    CrossAxisAlignment.End => crossAxisSize - childCrossAxisSize,
                    _ => 0, // Start and Stretch align to 0
                };
                var newLayoutData = ChildrenLayoutBuffer[i];
                newLayoutData.Position = isHorizontal
                    ? new Vector2(mainAxisPos, crossAxisPos)
                    : new Vector2(crossAxisPos, mainAxisPos);
                ChildrenLayoutBuffer[i] = newLayoutData;
                mainAxisPos += (isHorizontal ? layout.Size.x : layout.Size.y) + alignmentSpacing;

                if (i < ChildrenLayoutBuffer.Count - 1)
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
            else // Max of heights (for a Row): mirror PerformSizing's flex distribution so each
            // child is measured at the width it will actually receive, not at infinity.
            {
                var totalFlex = 0;
                var inflexibleWidth = 0f;
                var maxHeight = 0f;

                foreach (var child in _state.Children)
                {
                    if (TryGetFlex(child, isHorizontal: true, out var flex, out _))
                    {
                        totalFlex += flex;
                        continue;
                    }

                    var childWidth = GetChildIntrinsicWidth(child, float.PositiveInfinity);
                    inflexibleWidth += childWidth;
                    maxHeight = Mathf.Max(maxHeight, GetChildIntrinsicHeight(child, childWidth));
                }

                if (_state.Children.Length > 0)
                {
                    inflexibleWidth += Mathf.Max(0, _state.Children.Length - 1) * _state.Spacing;
                }

                if (totalFlex > 0)
                {
                    var spacePerFlex = float.IsInfinity(width)
                        ? float.PositiveInfinity
                        : Mathf.Max(0, width - inflexibleWidth) / totalFlex;

                    foreach (var child in _state.Children)
                    {
                        if (!TryGetFlex(child, isHorizontal: true, out var flex, out _))
                            continue;

                        var childWidth = float.IsInfinity(spacePerFlex)
                            ? float.PositiveInfinity
                            : spacePerFlex * flex;
                        maxHeight = Mathf.Max(
                            maxHeight,
                            GetChildIntrinsicHeight(child, childWidth)
                        );
                    }
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

                if (_state.Children.Length > 0)
                {
                    totalWidth += Mathf.Max(0, _state.Children.Length - 1) * _state.Spacing;
                }

                return totalWidth;
            }
            else // Max of widths (for a Column): mirror PerformSizing's flex distribution so each
            // child is measured at the height it will actually receive, not at infinity.
            {
                var totalFlex = 0;
                var inflexibleHeight = 0f;
                var maxWidth = 0f;

                foreach (var child in _state.Children)
                {
                    if (TryGetFlex(child, isHorizontal: false, out var flex, out _))
                    {
                        totalFlex += flex;
                        continue;
                    }

                    var childHeight = GetChildIntrinsicHeight(child, float.PositiveInfinity);
                    inflexibleHeight += childHeight;
                    maxWidth = Mathf.Max(maxWidth, GetChildIntrinsicWidth(child, childHeight));
                }

                if (_state.Children.Length > 0)
                {
                    inflexibleHeight += Mathf.Max(0, _state.Children.Length - 1) * _state.Spacing;
                }

                if (totalFlex > 0)
                {
                    var spacePerFlex = float.IsInfinity(height)
                        ? float.PositiveInfinity
                        : Mathf.Max(0, height - inflexibleHeight) / totalFlex;

                    foreach (var child in _state.Children)
                    {
                        if (!TryGetFlex(child, isHorizontal: false, out var flex, out _))
                            continue;

                        var childHeight = float.IsInfinity(spacePerFlex)
                            ? float.PositiveInfinity
                            : spacePerFlex * flex;
                        maxWidth = Mathf.Max(maxWidth, GetChildIntrinsicWidth(child, childHeight));
                    }
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

        /// <summary>
        ///     The inflexible child taking the most room on the main axis, or -1 if there are none.
        /// </summary>
        /// <remarks>
        ///     A hint, and labelled as one in the message: the biggest child is where the room went, not
        ///     necessarily whose fault it is. It replaces naming the <i>last</i> inflexible child, which
        ///     is a position rather than a cause -- a Row bounded to 100 with children 200 and 5 blamed
        ///     the 5.
        ///     <para>
        ///         Only ever evaluated as an argument to a reporting facade, so <c>[Conditional]</c>
        ///         deletes the scan along with the call: it costs nothing unless something is wrong.
        ///     </para>
        /// </remarks>
        private int LargestNonFlexChild()
        {
            var isHorizontal = _axis == Axis.Horizontal;
            var largest = -1;
            var largestExtent = float.NegativeInfinity;

            foreach (var i in _nonFlexChildrenIndices)
            {
                var size = ChildrenLayoutBuffer[i].Size;
                var extent = isHorizontal ? size.x : size.y;

                if (extent > largestExtent)
                {
                    largestExtent = extent;
                    largest = i;
                }
            }

            return largest;
        }

        // Shared with PerformSizing() so the intrinsic-size estimate can never silently diverge
        // from what the real flex layout pass actually does.
        private static bool TryGetFlex(
            IState child,
            bool isHorizontal,
            out int flex,
            out FlexFit fit
        )
        {
            // -- remark: we use InnerViewState because Expanded might be composed inside other Hoc widgets
            if (child.InnerViewState is FlexibleState flexible)
            {
                flex = flexible.Flex;
                fit = flexible.Fit;
                return true;
            }

            flex = 0;
            fit = FlexFit.Tight;
            return false;
        }

        // Remedies live next to the algorithm that knows the fix. An unbounded axis is not fixed the
        // same way in a flex, an anchored box and a pan surface, so there is nothing here to derive
        // from the code alone.

        private const string WrapInFlexible =
            "Wrap it in Expanded or Flexible so it is given a bounded main axis.";

        private const string MakeRoomOrScroll =
            "Give the flex a bounded main axis, wrap a child in Expanded/Flexible, or let it scroll.";

        private const string BoundTheCrossAxisOrDoNotStretch =
            "CrossAxisAlignment.Stretch has nothing to stretch to while the cross axis is unbounded. "
            + "Bound it from an ancestor (Expanded, SizedBox, or a fixed-size parent), or use "
            + "CrossAxisAlignment.Start so the children keep their own size.";

        private const string BoundTheMainAxis =
            "An ancestor must bound this axis (Expanded, SizedBox, or a fixed-size parent), "
            + "or the flexible children must be removed.";
    }
}
