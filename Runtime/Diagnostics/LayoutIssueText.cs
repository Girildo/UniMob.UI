using System;
using System.Text;
using UniMob.UI.Internal.Views;
using UnityEngine;

namespace UniMob.UI.Diagnostics
{
    /// <summary>
    ///     Turns a <see cref="LayoutIssue"/> into sentences, and decides how loud it is.
    /// </summary>
    /// <remarks>
    ///     Severity is derived rather than carried. Every site's level was already a function of its
    ///     code, with exactly one accidental divergence -- a host render object warning where the
    ///     package errored for the same fault -- and deriving it here makes that unrepresentable.
    ///     <para>
    ///         Line 1 is one whole sentence naming the widget and the number, because Unity's console
    ///         list shows only the first line of an entry. Everything else goes below it.
    ///     </para>
    ///     <para>
    ///         Public rather than internal, for the two reasons that outrank a smaller surface: a
    ///         reporter outside this assembly has to reach the same severity or the divergence comes
    ///         straight back, and there is no <c>InternalsVisibleTo</c> here, so an internal formatter
    ///         is an untested one.
    ///     </para>
    /// </remarks>
    public static class LayoutIssueText
    {
        public static LogType Severity(LayoutIssueCode code)
        {
            // Both overflows are a layout that still renders, wrongly and visibly. The other three are
            // a widget that will not render at all.
            return
                code
                    is LayoutIssueCode.Overflow
                        or LayoutIssueCode.ContentOverflow
                        or LayoutIssueCode.ChildOutOfBounds
                ? LogType.Warning
                : LogType.Error;
        }

        public static string Summary(in LayoutIssue issue)
        {
            using (Atom.NoWatch)
            {
                var subject = DiagnosticNode.NameOf(issue.Subject);
                var where = AxisPhrase(issue.Axes);

                switch (issue.Code)
                {
                    case LayoutIssueCode.Overflow:
                        return $"{subject} overflowed by {issue.Amount:F1}px on the {where}.";

                    case LayoutIssueCode.ChildOutOfBounds:
                        var strayChild = issue.Culprit is null
                            ? "A child"
                            : DiagnosticNode.NameOf(issue.Culprit);
                        return $"{strayChild} is drawn {issue.Amount:F1}px outside {subject} "
                            + $"on the {where}.";

                    case LayoutIssueCode.ContentOverflow:
                        return $"{subject} needs {issue.Amount:F1}px more on the {where} "
                            + "than it was given, and is drawn outside its box.";

                    case LayoutIssueCode.UnboundedConstraint:
                        return $"{subject} needs a bounded {where} and was given an unbounded one.";

                    case LayoutIssueCode.NonFiniteChildSize:
                        var culprit = issue.Culprit is null
                            ? "a child"
                            : DiagnosticNode.NameOf(issue.Culprit);
                        return $"{culprit} answered {subject} with a non-finite size on the {where}.";

                    case LayoutIssueCode.SizeExceedsConstraints:
                        return $"{subject} answered {issue.Amount:F1}px larger than its constraints "
                            + $"allowed on the {where}.";

                    case LayoutIssueCode.NonFiniteSize:
                        return $"{subject} answered with a non-finite size on the {where}, "
                            + "under a finite maximum.";

                    default:
                        return $"{subject} reported {issue.Code} on the {where}.";
                }
            }
        }

        /// <summary>The whole entry, plain: summary, then where, who, what to do, and the numbers.</summary>
        public static string Compose(in LayoutIssue issue)
        {
            var builder = new StringBuilder();

            using (Atom.NoWatch)
            {
                builder.Append(Summary(issue)).Append('\n');
                Row(builder, "at", WidgetPath.From(issue.Subject));

                if (issue.Culprit != null)
                {
                    // "largest child", not "culprit": for an overflow the biggest inflexible child is
                    // a hint about where the room went, not a verdict about whose fault it is.
                    var label = issue.Code == LayoutIssueCode.Overflow ? "largest" : "child";
                    Row(builder, label, Culprit(issue));
                }

                if (!string.IsNullOrEmpty(issue.Remedy))
                {
                    Row(builder, "fix", issue.Remedy);
                }

                if (issue.Constraints.HasValue || issue.Size.HasValue)
                {
                    var constraints = issue.Constraints.HasValue
                        ? issue.Constraints.Value.ToString()
                        : "<not laid out>";
                    var size = issue.Size.HasValue ? issue.Size.Value.ToString() : "<no size yet>";
                    Row(builder, "got", $"{constraints}  ->  {size}");
                }

                var children = ChildSizes(issue.Subject);
                if (children != null)
                {
                    // "measured", not "gave". A report fired from inside a sizing pass sees the
                    // children the algorithm has already reached at this pass's sizes and the rest at
                    // their previous ones -- for an overflow that means the inflexible children are
                    // current (and are the ones that overflowed) while the flexible ones are stale.
                    // Naming the line for what it is beats printing last frame's numbers as this
                    // frame's.
                    Row(builder, "measured", children);
                }
            }

            return builder.ToString();
        }

        private static void Row(StringBuilder builder, string label, string value)
        {
            builder.Append("  ").Append(label.PadRight(8)).Append(value).Append('\n');
        }

        // Only reached where Culprit is set; the caller checks before it builds the row.
        private static string Culprit(in LayoutIssue issue)
        {
            var culprit = issue.Culprit!;
            var described = DiagnosticNode.Describe(culprit);
            var size = culprit.RenderObject is null
                ? string.Empty
                : $"  {culprit.RenderObject.PeekSize()}";

            if (issue.Subject is not IMultiChildLayoutState multiChild)
            {
                return described + size;
            }

            try
            {
                var children = multiChild.Children;
                for (var i = 0; i < children.Length; i++)
                {
                    if (ReferenceEquals(children[i], issue.Culprit))
                    {
                        return $"child [{i}] of {children.Length}: {described}{size}";
                    }
                }
            }
            catch (Exception)
            {
                // A subject that cannot enumerate its children still has a culprit worth naming.
            }

            return described + size;
        }

        /// <summary>
        ///     One level of child sizes, read off each child's own render object. Null when the subject
        ///     has no ordered children to report.
        /// </summary>
        /// <remarks>
        ///     Each child's own size rather than the parent's per-child record, which is protected and
        ///     reachable only through a getter that drives a pass. Where the two differ -- a parent that
        ///     clamped what a child answered -- the child's own answer is the more useful of the two,
        ///     because it is the one that caused the report.
        ///     <para>
        ///         These are last-measured sizes, not necessarily this pass's: a child the reporting
        ///         algorithm has not reached yet still holds the size it was given last time. The line
        ///         is labelled "measured" for that reason.
        ///     </para>
        /// </remarks>
        private static string? ChildSizes(IState? subject)
        {
            if (subject is not IMultiChildLayoutState multiChild)
            {
                return null;
            }

            try
            {
                var children = multiChild.Children;
                if (children.Length == 0)
                {
                    return null;
                }

                var builder = new StringBuilder();
                for (var i = 0; i < children.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append("   ");
                    }

                    builder.Append('[').Append(i).Append("] ");

                    var child = children[i];
                    if (child is null)
                    {
                        builder.Append("<not built>");
                        continue;
                    }

                    builder.Append(DiagnosticNode.NameOf(child));

                    if (child.RenderObject != null)
                    {
                        builder.Append(' ').Append(child.RenderObject.PeekSize());
                    }
                }

                return builder.ToString();
            }
            catch (Exception ex)
            {
                return $"<children threw: {ex.GetType().Name}>";
            }
        }

        private static string AxisPhrase(LayoutAxes axes)
        {
            switch (axes)
            {
                case LayoutAxes.Horizontal:
                    return "horizontal axis";
                case LayoutAxes.Vertical:
                    return "vertical axis";
                case LayoutAxes.Both:
                    return "horizontal and vertical axes";
                default:
                    return "axis";
            }
        }
    }
}
