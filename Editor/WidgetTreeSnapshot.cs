using System;
using System.Collections.Generic;
using UniMob.UI.Diagnostics;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.EventSystems;

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

        /// <summary>
        ///     The node owning each mounted GameObject, for turning a raycast hit back into a widget.
        /// </summary>
        /// <remarks>
        ///     A wrapper and the state it wraps resolve to the same view, so several nodes can claim one
        ///     GameObject. Capture is pre-order, so the last write is the deepest of them -- which is the
        ///     one that actually owns the view, and the more useful answer.
        /// </remarks>
        private readonly Dictionary<GameObject, Node> _byTarget =
            new Dictionary<GameObject, Node>();

        private static readonly List<RaycastResult> SharedRaycastResults =
            new List<RaycastResult>();

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

            if (node.Target != null)
            {
                _byTarget[node.Target] = node;
            }

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
                // Named rather than an anonymous "...", because a truncated tree and a tree that
                // genuinely ends look identical otherwise, and the difference is the whole question
                // when widgets are reported missing.
                this.NodeCount++;
                node.Children.Add(
                    new Node
                    {
                        Label = $"<depth limit {maxDepth} reached -- raise it to see deeper>",
                        HasIssue = true,
                    }
                );
                node.SubtreeHasIssue = true;
                return node;
            }

            // Read without a guard that swallows. Everything else here degrades to a placeholder
            // because one unreadable label must not cost a row, but children are different in kind: a
            // failure here removes an entire subtree, and doing that silently makes the window lie
            // about what the app contains -- which is the one thing it exists not to do. A virtualized
            // list's children are an atom recompute, so this is a live risk rather than a theoretical
            // one.
            IReadOnlyList<IState> children;
            try
            {
                children = LayoutTree.ChildrenOf(state);
            }
            catch (Exception ex)
            {
                this.NodeCount++;
                node.Children.Add(
                    new Node
                    {
                        Label = $"<children threw: {ex.GetType().Name}: {ex.Message}>",
                        HasIssue = true,
                    }
                );
                node.SubtreeHasIssue = true;
                return node;
            }

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
        ///     The widget the pointer is actually on, or null.
        /// </summary>
        /// <remarks>
        ///     <b>uGUI's own raycast decides this, which is the whole point.</b> Comparing boxes cannot
        ///     answer "what am I pointing at": a box says where a widget would be if it were drawn, and
        ///     knows nothing about masks, canvas sorting, or whether anything paints there. A scroll
        ///     grid is the case that proves it -- its cards are clipped by a mask the boxes cannot see,
        ///     so a box test hands you whatever large thing happens to overlap them.
        ///     <para>
        ///         The raycast returns what a real click would hit, topmost first, so the answer already
        ///         accounts for every one of those. Results that belong to no widget -- the picker's own
        ///         click-catcher, most obviously -- map to nothing and are skipped, which is why the
        ///         overlay does not have to be torn down to ask this question.
        ///     </para>
        ///     <para>
        ///         The cost is that it only reports Graphics with <c>raycastTarget</c> set, so a widget
        ///         that paints nothing is never the answer. That is the correct trade for a picker and it
        ///         is what a browser's element picker does too: you point at something painted and walk
        ///         up the tree from there, which this window is for. The box walk below still covers the
        ///         points a raycast cannot answer.
        ///     </para>
        /// </remarks>
        /// <param name="geometric">
        ///     Skip the raycast and go by layout boxes alone. The escape hatch for the widgets a click
        ///     cannot reach: anything below an <c>IgnorePointer</c> (its view drops
        ///     <c>blocksRaycasts</c>, and the raycast then lands on whatever is behind it rather than
        ///     coming back empty, so the fallback never fires), a <c>CustomPaint</c> (its image sets
        ///     <c>raycastTarget = false</c>), and anything else that paints without accepting input.
        /// </param>
        public Node HitTest(Vector2 screenPoint, bool geometric = false)
        {
            var painted = geometric ? null : RaycastForNode(screenPoint);
            if (painted != null)
            {
                return painted;
            }

            // Nothing painted under the pointer, or asked for boxes. Topmost leaf first: only leaves
            // claim a point, so a positioning container -- an Align with no size factor is a full-screen
            // box around a 120px rail -- passes through instead of swallowing the screen.
            for (var i = this.Roots.Count - 1; i >= 0; i--)
            {
                var leaf = HitLeaf(this.Roots[i], screenPoint);
                if (leaf != null)
                {
                    return leaf;
                }
            }

            // Nothing under the pointer bottoms out in a leaf -- the gap inside a container, mostly.
            // Falling back to the deepest containing node keeps every point answerable, and makes this
            // strictly an improvement: where there is a leaf you get the topmost one, and where there
            // is not you get the same node this window has always given.
            Node deepest = null;
            var deepestLevel = -1;

            foreach (var root in this.Roots)
            {
                HitDeepest(root, screenPoint, 0, ref deepest, ref deepestLevel);
            }

            return deepest;
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
            if (node == null)
            {
                return Rect.zero;
            }

            if (node.Target == null || node.Target.transform is not RectTransform rect)
            {
                // Reached by a node that is not mounted to a view: a sliver's cache window, or a
                // read taken before the first render. A widget that merely builds another is not
                // one of them -- HocState and StatelessElement forward InnerViewState to the child
                // they build, so they answer with that child's view. The extent is the union of the
                // view-backed widgets beneath, which for a wrapper is the area it governs.
                var union = Rect.zero;

                foreach (var child in node.Children)
                {
                    var childRect = ScreenRectOf(child);
                    if (childRect.width <= 0f && childRect.height <= 0f)
                    {
                        continue;
                    }

                    union =
                        union.width <= 0f && union.height <= 0f
                            ? childRect
                            : Rect.MinMaxRect(
                                Mathf.Min(union.xMin, childRect.xMin),
                                Mathf.Min(union.yMin, childRect.yMin),
                                Mathf.Max(union.xMax, childRect.xMax),
                                Mathf.Max(union.yMax, childRect.yMax)
                            );
                }

                return union;
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

        /// <summary>
        ///     What a click at <paramref name="screenPoint"/> would land on, as a widget.
        /// </summary>
        private Node RaycastForNode(Vector2 screenPoint)
        {
            var events = EventSystem.current;
            if (events == null)
            {
                return null;
            }

            SharedRaycastResults.Clear();
            events.RaycastAll(
                new PointerEventData(events) { position = screenPoint },
                SharedRaycastResults
            );

            // Topmost first. The first result belonging to a widget wins; earlier ones that belong to
            // nothing in this tree are the picker's own pieces and anything else the scene draws on top.
            for (var i = 0; i < SharedRaycastResults.Count; i++)
            {
                var node = NodeForGameObject(SharedRaycastResults[i].gameObject);
                if (node != null)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        ///     The widget owning <paramref name="target"/>, or the nearest one above it.
        /// </summary>
        /// <remarks>
        ///     A raycast reports the GameObject carrying the Graphic, which is often a piece of a view's
        ///     own prefab rather than the view root a widget is mapped to. Walking up finds the widget
        ///     that owns it instead of answering "no widget here".
        /// </remarks>
        private Node NodeForGameObject(GameObject target)
        {
            for (var t = target != null ? target.transform : null; t != null; t = t.parent)
            {
                if (_byTarget.TryGetValue(t.gameObject, out var node))
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        ///     The last leaf, in paint order, whose box contains the point.
        /// </summary>
        /// <remarks>
        ///     Children last-first and returning on the first hit, so the first answer found is the one
        ///     drawn last. Nothing is pruned on a parent's box: a positioned child or an anchored box
        ///     may sit outside its parent on purpose, and pruning would make those unpickable.
        /// </remarks>
        private static Node HitLeaf(Node node, Vector2 screenPoint)
        {
            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                var hit = HitLeaf(node.Children[i], screenPoint);
                if (hit != null)
                {
                    return hit;
                }
            }

            return node.Children.Count == 0 && Contains(node, screenPoint) ? node : null;
        }

        private static void HitDeepest(
            Node node,
            Vector2 screenPoint,
            int level,
            ref Node best,
            ref int bestLevel
        )
        {
            if (Contains(node, screenPoint) && level > bestLevel)
            {
                best = node;
                bestLevel = level;
            }

            foreach (var child in node.Children)
            {
                HitDeepest(child, screenPoint, level + 1, ref best, ref bestLevel);
            }
        }

        /// <summary>False for a node mounted to no view, which has no box of its own to contain anything.</summary>
        private static bool Contains(Node node, Vector2 screenPoint)
        {
            return node.Target != null
                && node.Target.transform is RectTransform rect
                && RectTransformUtility.RectangleContainsScreenPoint(
                    rect,
                    screenPoint,
                    CameraFor(node.Target)
                );
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
