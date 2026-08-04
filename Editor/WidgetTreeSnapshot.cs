using System;
using System.Collections.Generic;
using UniMob.UI.Diagnostics;
using UniMob.UI.Layout.Internal.RenderObjects;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Editor
{
    /// <summary>
    ///     A dead copy of the widget tree, taken at one instant, holding nothing that can come back to
    ///     life.
    /// </summary>
    /// <remarks>
    ///     An editor window reading a live reactive tree is the same hazard as the diagnostic that once
    ///     subscribed its own layout atom to eight ancestors. The rules that keep this inert:
    ///     <list type="bullet">
    ///         <item>Everything is read once, inside <c>Atom.NoWatch</c>, so nothing is subscribed to.</item>
    ///         <item>
    ///             No <see cref="IState"/> is retained. States are disposed as the user navigates, and a
    ///             window holding one across refreshes would be reading a corpse. Anything the UI needs
    ///             later -- the label, the numbers, the GameObject to ping -- is resolved now and copied.
    ///         </item>
    ///         <item>
    ///             Constraints come from <c>RenderObject.Constraints</c> (an atom, hence NoWatch) and the
    ///             size from <c>PeekSize()</c>, never <c>WatchLayout()</c> or <c>ChildrenLayout</c>,
    ///             whose getters drive a layout pass. A window must never be able to make the app lay
    ///             out.
    ///         </item>
    ///     </list>
    /// </remarks>
    internal sealed class WidgetTreeSnapshot
    {
        public sealed class Node
        {
            public string Label;
            public string Constraints;
            public string Size;

            /// <summary>This node's own fault, if it reported one during the last pass.</summary>
            public bool HasIssue;

            /// <summary>
            ///     Whether anything in this subtree has a fault, so a collapsed branch still shows that
            ///     it is worth opening. Without this a window is only useful once you have already
            ///     guessed where to look.
            /// </summary>
            public bool SubtreeHasIssue;

            /// <summary>Null for a build-only widget, which has no GameObject to select.</summary>
            public GameObject Target;

            public int Index = -1;
            public readonly List<Node> Children = new List<Node>();
        }

        public readonly List<Node> Roots = new List<Node>();

        public int NodeCount { get; private set; }

        public static WidgetTreeSnapshot Take(int maxDepth)
        {
            var snapshot = new WidgetTreeSnapshot();

            using (Atom.NoWatch)
            {
                var panels = UnityEngine.Object.FindObjectsByType<ViewPanel>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None
                );

                foreach (var panel in panels)
                {
                    // A panel that has never been rendered has no root yet -- pooled panels and
                    // prefab instances sit in the scene like this. They are not empty trees, they are
                    // not trees, so they get no row rather than a row saying nothing.
                    if (panel.LayoutRoot is null)
                    {
                        continue;
                    }

                    snapshot.Roots.Add(
                        snapshot.Capture(panel.LayoutRoot, depth: 0, maxDepth, index: -1)
                    );
                }
            }

            return snapshot;
        }

        private Node Capture(IState state, int depth, int maxDepth, int index)
        {
            if (state is null)
            {
                // A virtualized list's realized window has holes in it. Shown rather than skipped, so
                // the indices keep meaning what they say.
                this.NodeCount++;
                return new Node { Label = "<not built>", Index = index };
            }

            var node = new Node { Index = index };
            this.NodeCount++;

            // Each channel guarded on its own. A state can be mid-construction or disposed, and one
            // member throwing must not cost the whole row.
            node.Label = Guarded(() => DiagnosticNode.Describe(state), "<label threw>");
            node.Target = Guarded(() => state.InnerViewState?.MountedView?.gameObject, null);

            var render = Guarded(() => state.RenderObject, null);
            if (render != null)
            {
                node.Constraints = Guarded(
                    () => render.Constraints?.ToString() ?? "<not laid out>",
                    "<threw>"
                );
                node.Size = Guarded(() => render.PeekSize().ToString(), "<threw>");
                node.HasIssue = Guarded(() => render.HasLayoutIssue, false);
            }

            node.SubtreeHasIssue = node.HasIssue;

            if (depth >= maxDepth)
            {
                node.Children.Add(new Node { Label = "..." });
                return node;
            }

            var children = Guarded(() => LayoutTree.ChildrenOf(state), Array.Empty<IState>());
            for (var i = 0; i < children.Count; i++)
            {
                // An index only earns its place where there are siblings to tell apart. On an only
                // child it is "[0]" on every second row of a deep chain of wrappers, which is most of
                // this tree, and it reads as noise because it is.
                var child = Capture(children[i], depth + 1, maxDepth, children.Count > 1 ? i : -1);
                node.Children.Add(child);
                node.SubtreeHasIssue |= child.SubtreeHasIssue;
            }

            return node;
        }

        private static T Guarded<T>(Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
