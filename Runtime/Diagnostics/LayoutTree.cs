using System;
using System.Text;
using JetBrains.Annotations;
using UniMob.UI.Layout;
using UniMob.UI.Layout.Internal.Views;

namespace UniMob.UI.Diagnostics
{
    /// <summary>
    ///     Everything below a widget: one indented line per node, with the constraints it was given and
    ///     the size it answered with.
    /// </summary>
    /// <remarks>
    ///     On demand only. Unlike <see cref="WidgetPath"/> this must never be called from inside a
    ///     layout pass: reading a virtualized list's children is an atom recompute, and there is no
    ///     amount of <c>Atom.NoWatch</c> that makes recomputing cheap. <c>NoWatch</c> is still applied,
    ///     because a render object's constraints are an atom and an editor window reading this must not
    ///     join the app's dependency graph.
    ///     <para>
    ///         Walks the state tree rather than a parent's per-child layout buffer. The buffer would
    ///         mean reaching <c>ChildrenLayout</c>, whose getter drives a pass, and it holds sizes only
    ///         -- each child's own render object has the constraints as well.
    ///     </para>
    /// </remarks>
    public static class LayoutTree
    {
        public const int DefaultMaxDepth = 8;

        public static string Describe([CanBeNull] IState root, int maxDepth = DefaultMaxDepth)
        {
            var builder = new StringBuilder();

            using (Atom.NoWatch)
            {
                Append(builder, root, depth: 0, maxDepth, index: -1);
            }

            return builder.ToString();
        }

        private static void Append(
            StringBuilder builder,
            [CanBeNull] IState state,
            int depth,
            int maxDepth,
            int index
        )
        {
            builder.Append(' ', depth * 2);

            if (index >= 0)
            {
                builder.Append('[').Append(index).Append("] ");
            }

            // A virtualized list hands back the realized window, and an index inside it that has not
            // been built yet is null. Rendered rather than skipped, so the numbering keeps meaning
            // what it says.
            if (state is null)
            {
                builder.Append("<not built>").Append('\n');
                return;
            }

            DiagnosticNode.AppendTo(builder, state);
            AppendGeometry(builder, state);
            builder.Append('\n');

            if (depth >= maxDepth)
            {
                builder.Append(' ', (depth + 1) * 2).Append("...").Append('\n');
                return;
            }

            AppendChildren(builder, state, depth, maxDepth);
        }

        private static void AppendChildren(
            StringBuilder builder,
            IState state,
            int depth,
            int maxDepth
        )
        {
            try
            {
                switch (state)
                {
                    case IMultiChildLayoutState multiChild:
                        var children = multiChild.Children;
                        for (var i = 0; i < children.Length; i++)
                        {
                            Append(builder, children[i], depth + 1, maxDepth, i);
                        }

                        break;

                    case ISingleChildLayoutState singleChild:
                        Append(builder, singleChild.Child, depth + 1, maxDepth, index: 0);
                        break;
                }
            }
            catch (Exception ex)
            {
                builder
                    .Append(' ', (depth + 1) * 2)
                    .Append("<children threw: ")
                    .Append(ex.GetType().Name)
                    .Append(">\n");
            }
        }

        private static void AppendGeometry(StringBuilder builder, IState state)
        {
            try
            {
                var renderObject = state.RenderObject;
                if (renderObject is null)
                {
                    builder.Append("  <no render object>");
                    return;
                }

                var constraints = renderObject.Constraints;
                builder.Append("  ");

                // Printed as an absence rather than as Tight(0,0), which is a plausible-looking box.
                builder.Append(constraints.HasValue ? constraints.Value.ToString() : "<not laid out>");
                builder.Append("  ->  ").Append(renderObject.Size);
            }
            catch (Exception ex)
            {
                builder.Append("  <geometry threw: ").Append(ex.GetType().Name).Append('>');
            }
        }
    }
}
