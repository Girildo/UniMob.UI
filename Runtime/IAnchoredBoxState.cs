using UniMob.UI.Rendering;
using UnityEngine;

namespace UniMob.UI
{
    /// <summary>
    /// What <see cref="RenderAnchoredBox"/> needs from its state to place a child against another
    /// widget's measured box. All of the geometry is reactive: the layout pass runs inside an atom,
    /// so reading it here is what makes the child follow the anchor.
    /// </summary>
    public interface IAnchoredBoxState : ISingleChildLayoutState
    {
        /// <summary><b>[Atom]</b> The box being anchored to, in canvas space.</summary>
        WidgetGeometry AnchorGeometry { get; }

        /// <summary><b>[Atom]</b> This widget's own box, in canvas space, to rebase the anchor into.</summary>
        WidgetGeometry SelfGeometry { get; }

        // The PREFERRED placement. The render object mirrors it across FlipToFit's axis when the
        // child turns out not to fit on that side, because that is the one place the child's
        // measured size exists.
        Alignment TargetAnchor { get; }
        Alignment ChildAnchor { get; }
        Vector2 Offset { get; }
        float? KeepInsidePadding { get; }
        bool MatchAnchorWidth { get; }

        /// <summary>The axis the placement may flip on, or null to place it exactly as described.</summary>
        Axis? FlipToFit { get; }
    }
}
