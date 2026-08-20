using System;
using UnityEngine;

namespace UniMob.UI.Layout.Internal.RenderObjects
{
    /// <summary>
    /// A generic render object that acts as a proxy for another render object, forwarding layout and rendering
    /// to its child. This is useful for creating wrapper widgets that modify the behavior of their child
    /// without changing its layout logic (e.g. a clickable button that sizes itself to its child).
    /// </summary>
    public class RenderAspectRatio : RenderProxy
    {
        private float _aspectRatio;

        public RenderAspectRatio(IAspectRatioState state)
            : base(state)
        {
            // Intentionally left blank.
            this._aspectRatio = state.AspectRatio;
        }

        // The aspect ratio is defined as width / height.
        // Mimicks the behavior of Flutter's AspectRatio widget, which sizes itself to a specific aspect ratio,
        // trying to infer height from width if possible, but falling back to inferring width from height if width is unconstrained.
        // If the constraints are tight, we must respect them and cannot satisfy the aspect ratio.
        // See: https://github.com/flutter/flutter/blob/main/packages/flutter/lib/src/rendering/proxy_box.dart#L450
        private Vector2 ApplyAspectRatio(LayoutConstraints constraints)
        {
            // If constraints are already tight, we must respect them and cannot satisfy the aspect ratio.
            if (constraints.HasTightHeight && constraints.HasTightWidth)
            {
                return constraints.Constrain(Vector2.zero);
            }

            var width = constraints.MaxWidth;
            float height;

            // We default to maximizing width, then compute height.
            if (constraints.HasBoundedWidth)
            {
                height = width / _aspectRatio;
            }
            else
            {
                // If width is unconstrained, maximize height instead.
                height = constraints.MaxHeight;
                width = height * _aspectRatio;
            }

            // Now we check if the computed dimensions violate any constraints,
            // scaling both axes proportionately if they do.
            if (width > constraints.MaxWidth)
            {
                width = constraints.MaxWidth;
                height = width / _aspectRatio;
            }

            if (height > constraints.MaxHeight)
            {
                height = constraints.MaxHeight;
                width = height * _aspectRatio;
            }

            if (width < constraints.MinWidth)
            {
                width = constraints.MinWidth;
                height = width / _aspectRatio;
            }

            if (height < constraints.MinHeight)
            {
                height = constraints.MinHeight;
                width = height * _aspectRatio;
            }

            var desired = new Vector2(width, height);

            // The four adjustments above run in order, so raising one axis to its minimum can push the
            // other back over its maximum: an aspect ratio is not satisfiable inside every box. Where
            // it is not, Constrain keeps the box and silently breaks the ratio.
            ReportContentOverflow(constraints, desired, TheRatioDoesNotFit);

            return constraints.Constrain(desired);
        }

        private const string TheRatioDoesNotFit =
            "This aspect ratio cannot be satisfied inside the given box, so the box wins and the ratio "
            + "is broken. Loosen the box's minimum on one axis, or change the ratio.";

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            var size = ApplyAspectRatio(constraints);

            if (Child != null)
            {
                // AspectRatio forces its child to perfectly match its computed size
                var childConstraints = LayoutConstraints.Tight(size.x, size.y);
                ChildSize = LayoutChild(Child, childConstraints);
            }
            else
            {
                ChildSize = Vector2.zero;
            }

            return size;
        }

        protected override float ComputeIntrinsicWidth(float height)
        {
            if (float.IsPositiveInfinity(height))
                return 0; // Cannot determine intrinsic width from infinite height

            return height * _aspectRatio;
        }

        protected override float ComputeIntrinsicHeight(float width)
        {
            if (float.IsPositiveInfinity(width))
                return 0; // Cannot determine intrinsic height from infinite width

            return width / _aspectRatio;
        }
    }

    public interface IAspectRatioState : ISingleChildLayoutState
    {
        // Intentionally left blank.
        public float AspectRatio { get; }
    }
}
