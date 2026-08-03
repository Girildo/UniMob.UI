using System;
using System.Collections.Generic;
using System.Text;
using JetBrains.Annotations;

namespace UniMob.UI.Diagnostics
{
    /// <summary>
    ///     Where a widget is: the chain of ancestors above it, root first.
    ///     <c>App &gt; HomeScreen &gt; Padding &gt; Row#actions</c>.
    /// </summary>
    /// <remarks>
    ///     This is the half of a layout error that names the place rather than the fault, and it is
    ///     read from inside a sizing pass, so the whole walk is under <c>Atom.NoWatch</c>: a render
    ///     object's constraints are an atom, and describing an ancestor must not make the reporter
    ///     depend on it.
    /// </remarks>
    public static class WidgetPath
    {
        // Deep enough to reach a recognisable screen from a leaf, short enough that the first line of
        // a report is still readable. Beyond it the path is truncated visibly rather than silently.
        public const int DefaultMaxDepth = 12;

        private static readonly List<IState> SharedChain = new List<IState>();
        private static readonly StringBuilder SharedBuilder = new StringBuilder();
        private static bool _walking;

        public static string From([CanBeNull] IState state, int maxDepth = DefaultMaxDepth)
        {
            if (state is null)
            {
                return "<null>";
            }

            // Composing a node runs the author's own label, and a label that reported would land back
            // here mid-walk and hand both callers a path spliced out of the other's. Reentrancy is
            // rare enough to answer with fresh buffers rather than by pooling per depth.
            var reentrant = _walking;
            var chain = reentrant ? new List<IState>() : SharedChain;
            var builder = reentrant ? new StringBuilder() : SharedBuilder;
            _walking = true;

            try
            {
                chain.Clear();
                builder.Clear();

                using (Atom.NoWatch)
                {
                    var truncated = Collect(state, maxDepth, chain);

                    if (truncated)
                    {
                        builder.Append("... > ");
                    }

                    for (var i = chain.Count - 1; i >= 0; i--)
                    {
                        DiagnosticNode.AppendTo(builder, chain[i]);

                        if (i > 0)
                        {
                            builder.Append(" > ");
                        }
                    }
                }

                return builder.ToString();
            }
            finally
            {
                if (!reentrant)
                {
                    chain.Clear();
                    _walking = false;
                }
            }
        }

        /// <summary>Walks up from <paramref name="state"/>; true if it stopped short.</summary>
        private static bool Collect(IState state, int maxDepth, List<IState> chain)
        {
            var current = state;

            while (current != null)
            {
                if (chain.Count >= maxDepth)
                {
                    return true;
                }

                chain.Add(current);

                try
                {
                    // Null at the top of the tree, and on a state that was never mounted. A path that
                    // throws replaces the fault being reported with one of its own.
                    current = current.Context?.Parent?.State;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return false;
        }
    }
}
