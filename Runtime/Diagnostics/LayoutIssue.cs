using System;
using JetBrains.Annotations;
using UniMob.UI.Layout;
using UnityEngine;

namespace UniMob.UI.Diagnostics
{
    /// <summary>What went wrong. Four faults, and the last two are one push seen from both ends.</summary>
    public enum LayoutIssueCode
    {
        /// <summary>Children needed more room than there was.</summary>
        Overflow,

        /// <summary>An axis reached something that cannot work without a bound.</summary>
        UnboundedConstraint,

        /// <summary>A child answered with infinity or NaN.</summary>
        NonFiniteChildSize,

        /// <summary>A render object answered with infinity or NaN under a <i>finite</i> maximum.</summary>
        NonFiniteSize,
    }

    /// <summary>
    ///     Which axes a fault is on. <see cref="Axis"/> cannot say "both", which some faults are.
    /// </summary>
    [Flags]
    public enum LayoutAxes
    {
        None = 0,
        Horizontal = 1,
        Vertical = 2,
        Both = Horizontal | Vertical,
    }

    /// <summary>
    ///     One layout fault, as data. Composed only once a latch has decided this is worth saying, and
    ///     carries no formatted text: a reporter that writes to a console formats one way and one that
    ///     writes to a structured log formats another.
    /// </summary>
    /// <remarks>
    ///     There is deliberately no severity field. Every site's level is a function of its code, and the
    ///     one place the codebase disagreed with itself about that was an accident rather than a
    ///     decision, so deriving it makes the divergence unrepresentable.
    ///     <para>
    ///         <see cref="Remedy"/> is per-site rather than per-code, because an unbounded axis is fixed
    ///         differently in a flex, an anchored box and a pan surface. Deriving it from the code would
    ///         have to pick one of the three and be wrong about the other two.
    ///     </para>
    /// </remarks>
    public readonly struct LayoutIssue
    {
        public LayoutIssue(
            LayoutIssueCode code,
            [CanBeNull] IState subject,
            LayoutAxes axes,
            [CanBeNull] string remedy,
            [CanBeNull] IState culprit = null,
            LayoutConstraints? constraints = null,
            Vector2? size = null,
            float amount = 0f
        )
        {
            this.Code = code;
            this.Subject = subject;
            this.Axes = axes;
            this.Remedy = remedy;
            this.Culprit = culprit;
            this.Constraints = constraints;
            this.Size = size;
            this.Amount = amount;
        }

        public LayoutIssueCode Code { get; }

        /// <summary>The state that noticed. A role in this report, not a relationship.</summary>
        [CanBeNull]
        public IState Subject { get; }

        /// <summary>
        ///     The state this report blames, when that differs from the <see cref="Subject"/>: a Row is
        ///     the subject of an overflow, and the child that did not fit is the culprit.
        /// </summary>
        [CanBeNull]
        public IState Culprit { get; }

        public LayoutAxes Axes { get; }

        /// <summary>What the subject was given, if it is known at the point of the fault.</summary>
        public LayoutConstraints? Constraints { get; }

        /// <summary>What the subject answered with, if it had an answer yet.</summary>
        public Vector2? Size { get; }

        /// <summary>How much, where the fault has a magnitude: pixels overflowed, and nothing else so far.</summary>
        public float Amount { get; }

        /// <summary>What to change, written next to the algorithm that knows.</summary>
        [CanBeNull]
        public string Remedy { get; }
    }
}
