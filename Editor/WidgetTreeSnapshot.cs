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

            /// <summary>
            ///     Depth from the root, so a hit test can prefer the innermost widget under the
            ///     pointer. Nested rects all contain the point; only depth says which one you meant.
            /// </summary>
            public int Depth;

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

            var node = new Node { Index = index, Depth = depth };
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

        /// <summary>
        ///     The innermost widget whose box contains <paramref name="screenPoint"/>, or null.
        /// </summary>
        /// <remarks>
        ///     Deliberately not an <c>EventSystem</c> raycast. That only reports Graphics with
        ///     <c>raycastTarget</c> set, so it would miss every widget that paints nothing -- which is
        ///     most of a layout tree, and disproportionately the ones worth inspecting. Testing the
        ///     boxes directly finds a Column or a Padding exactly as readily as a button.
        ///     <para>
        ///         Deepest wins, because every ancestor's rect contains the point too and only depth
        ///         distinguishes what the user was aiming at.
        ///     </para>
        /// </remarks>
        public Node HitTest(Vector2 screenPoint)
        {
            Node best = null;

            foreach (var root in this.Roots)
            {
                HitTest(root, screenPoint, ref best);
            }

            return best;
        }

        /// <summary>
        ///     Where <paramref name="node"/> is on screen, or <see cref="Rect.zero"/> if it is nowhere.
        /// </summary>
        /// <remarks>
        ///     From the corners rather than from the layout size, because the question the highlight
        ///     answers is "where is this drawn", and any scale or rotation between here and the canvas
        ///     is part of that answer.
        /// </remarks>
        public static Rect ScreenRectOf(Node node)
        {
            if (node?.Target == null || node.Target.transform is not RectTransform rect)
            {
                return Rect.zero;
            }

            var camera = CameraFor(node.Target);
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var max = min;

            for (var i = 1; i < corners.Length; i++)
            {
                var point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>
        ///     A Screen Space - Overlay canvas resolves against no camera; anything else needs the one
        ///     its canvas renders through, or points land in the wrong space entirely.
        /// </summary>
        private static Camera CameraFor(GameObject target)
        {
            var canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return null;
            }

            // The ROOT canvas, not the nearest one. A scroll view nests a canvas for batching, and a
            // nested canvas reports its own serialized renderMode -- ScreenSpaceOverlay by default --
            // whatever the root is actually doing. Reading the nearest one therefore resolves a null
            // camera for precisely those subtrees, converting their points into the wrong space, and
            // every hit test inside a scrollable silently misses.
            var root = canvas.rootCanvas;

            if (root.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            // A world-space canvas often leaves worldCamera unset and is drawn by whatever camera
            // happens to see it. Guessing the main one is better than passing null, which would be
            // read as "overlay" and put every point in the wrong space.
            return root.worldCamera != null ? root.worldCamera : Camera.main;
        }

        private static void HitTest(Node node, Vector2 screenPoint, ref Node best)
        {
            if (node.Target != null && node.Target.transform is RectTransform rect)
            {
                if (
                    RectTransformUtility.RectangleContainsScreenPoint(
                        rect,
                        screenPoint,
                        CameraFor(node.Target)
                    ) && (best == null || node.Depth > best.Depth)
                )
                {
                    best = node;
                }
            }

            foreach (var child in node.Children)
            {
                HitTest(child, screenPoint, ref best);
            }
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
