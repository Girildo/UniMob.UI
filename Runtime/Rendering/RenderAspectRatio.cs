using System;
using UnityEngine;

namespace UniMob.UI.Rendering
{
    /// <summary>
    ///     Sizes itself to a given width/height ratio within the constraints it was handed, and forces
    ///     its child to exactly that size. Where the box cannot satisfy the ratio, the box wins.
    /// </summary>
    public class RenderAspectRatio : RenderProxy
    {
        private readonly IAspectRatioState _state;

        public RenderAspectRatio(IAspectRatioState state)
            : base(state)
        {
            _state = state;
        }

        // The aspect ratio is defined as width / height.
        // Mimicks the behavior of Flutter's AspectRatio widget, which sizes itself to a specific aspect ratio,
        // trying to infer height from width if possible, but falling back to inferring width from height if width is unconstrained.
        // If the constraints are tight, we must respect them and cannot satisfy the aspect ratio.
        // See: https://github.com/flutter/flutter/blob/09a3f858b7a67f78a36ebfd717160cebc3c79ac1/packages/flutter/lib/src/rendering/proxy_box.dart#L522
        private Vector2 ApplyAspectRatio(LayoutConstraints constraints)
        {
            // If constraints are already tight, we must respect them and cannot satisfy the aspect ratio.
            if (constraints.IsTight)
            {
                return constraints.Smallest;
            }

            var aspectRatio = _state.AspectRatio;
            var width = constraints.MaxWidth;
            float height;

            // We default to maximizing width, then compute height.
            if (constraints.HasBoundedWidth)
            {
                height = width / aspectRatio;
            }
            else
            {
                // If width is unconstrained, maximize height instead.
                height = constraints.MaxHeight;
                width = height * aspectRatio;
            }

            // Now we check if the computed dimensions violate any constraints,
            // scaling both axes proportionately if they do.
            if (width > constraints.MaxWidth)
            {
                width = constraints.MaxWidth;
                height = width / aspectRatio;
            }

            if (height > constraints.MaxHeight)
            {
                height = constraints.MaxHeight;
                width = height * aspectRatio;
            }

            if (width < constraints.MinWidth)
            {
                width = constraints.MinWidth;
                height = width / aspectRatio;
            }

            if (height < constraints.MinHeight)
            {
                height = constraints.MinHeight;
                width = height * aspectRatio;
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

        /// <summary>
        ///     The width the ratio implies for <paramref name="height"/>, or the child's own preferred
        ///     width where there is no height to derive one from.
        /// </summary>
        /// <remarks>
        ///     A ratio relates the two axes without fixing either, so an unbounded argument leaves it
        ///     nothing to scale and the child is the only thing left that knows a size. Answering zero
        ///     there reports a measurement rather than the absence of one, and a caller sizing a column
        ///     to its content cannot tell the two apart.
        /// </remarks>
        protected override float ComputeIntrinsicWidth(float height)
        {
            if (float.IsFinite(height))
            {
                return height * _state.AspectRatio;
            }

            return base.ComputeIntrinsicWidth(height);
        }

        /// <inheritdoc cref="ComputeIntrinsicWidth"/>
        protected override float ComputeIntrinsicHeight(float width)
        {
            if (float.IsFinite(width))
            {
                return width / _state.AspectRatio;
            }

            return base.ComputeIntrinsicHeight(width);
        }
    }
}
