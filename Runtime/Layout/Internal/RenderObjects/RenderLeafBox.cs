using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    /// <summary>
    /// Represents a render object that acts as a leaf node in the render tree, with fixed constraints and intrinsic
    /// sizing behavior.
    /// </summary>
    /// <remarks>A <see cref="RenderLeafBox"/> is a leaf node in the render tree, meaning it does not have any
    /// child render objects.  It sizes itself based on the provided <see cref="LayoutConstraints"/> and the specified
    /// <see cref="AxisSize"/>. The intrinsic dimensions of the leaf box are determined by the constraints and the axis
    /// size mode.</remarks>
    public class RenderLeafBox : RenderObject
    {
        private readonly LayoutConstraints _leafConstraints;
        private readonly AxisSize _axisSize;

        public RenderLeafBox(LayoutConstraints layoutConstraints) : this(layoutConstraints, AxisSize.Min)
        {
        }

        public RenderLeafBox(LayoutConstraints layoutConstraints, AxisSize axisSize)
        {
            if(axisSize == AxisSize.Max && (!layoutConstraints.HasBoundedHeight|| !layoutConstraints.HasBoundedWidth))
            {
                throw new ArgumentException("Leaf box must have bounded constraints.");
            }
            _leafConstraints = layoutConstraints;
            _axisSize = axisSize;
        }

        public override float GetIntrinsicHeight(float width)
        {
            return _axisSize == AxisSize.Min
                ? _leafConstraints.MinHeight
                : _leafConstraints.MaxHeight;
        }

        public override float GetIntrinsicWidth(float height)
        {
            return _axisSize == AxisSize.Min
                ? _leafConstraints.MinWidth
                : _leafConstraints.MaxWidth;
        }

        protected override void PerformPositioning()
        {
            // Nothing to do: leaf box has no children.
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            // We size ourself to the leaf constraints, constrained by the incoming constraints.
            var effectiveConstraints = constraints.Enforce(_leafConstraints);
            var vectorToConstrain = _axisSize == AxisSize.Min ? Vector2.zero : new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            return effectiveConstraints.Constrain(vectorToConstrain);
        }
    }
}
