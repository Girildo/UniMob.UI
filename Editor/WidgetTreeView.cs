using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace UniMob.UI.Editor
{
    /// <summary>
    ///     Draws a <see cref="WidgetTreeSnapshot"/>, on Unity's own tree control.
    /// </summary>
    /// <remarks>
    ///     Keyboard navigation, search, sticky selection, expand-all, resizable columns and framing a
    ///     row into view are all things <see cref="TreeView"/> already does. A hand-rolled row loop is a
    ///     worse version of each of them, and no amount of restyling closes that gap.
    ///     <para>
    ///         Item ids are assigned per <i>path</i> and remembered, so the same widget keeps the same
    ///         id across refreshes. TreeView stores expansion and selection by id, so anything keyed on
    ///         freshly-built objects would silently collapse the tree twice a second.
    ///     </para>
    /// </remarks>
    internal sealed class WidgetTreeView : TreeView
    {
        internal enum Column
        {
            Widget,
            Constraints,
            Size,
        }

        private readonly Dictionary<string, int> _idByPath = new Dictionary<string, int>();
        private readonly Dictionary<int, WidgetTreeSnapshot.Node> _nodeById =
            new Dictionary<int, WidgetTreeSnapshot.Node>();

        private WidgetTreeSnapshot _snapshot;
        private int _nextId = 1;

        public WidgetTreeView(TreeViewState state, MultiColumnHeader header)
            : base(state, header)
        {
            this.showAlternatingRowBackgrounds = true;
            this.showBorder = true;
            this.rowHeight = 18f;
            header.ResizeToFit();
        }

        /// <summary>Hides every branch with nothing to report, for when you already know there is one.</summary>
        public bool IssuesOnly { get; set; }

        /// <summary>Raised with the clicked node, or null when the selection is cleared.</summary>
        public event Action<WidgetTreeSnapshot.Node> NodeSelected;

        public void SetSnapshot(WidgetTreeSnapshot snapshot)
        {
            _snapshot = snapshot;
            Reload();
        }

        public static MultiColumnHeaderState CreateHeaderState() =>
            new MultiColumnHeaderState(
                new[]
                {
                    new MultiColumnHeaderState.Column
                    {
                        headerContent = new GUIContent("Widget"),
                        width = 420,
                        minWidth = 180,
                        autoResize = true,
                        canSort = false,
                    },
                    new MultiColumnHeaderState.Column
                    {
                        headerContent = new GUIContent("Constraints"),
                        width = 250,
                        minWidth = 90,
                        autoResize = false,
                        canSort = false,
                    },
                    new MultiColumnHeaderState.Column
                    {
                        headerContent = new GUIContent("Size"),
                        width = 110,
                        minWidth = 60,
                        autoResize = false,
                        canSort = false,
                    },
                }
            );

        /// <summary>The id showing <paramref name="target"/>, or 0 when nothing in the tree does.</summary>
        public int FindIdForGameObject(GameObject target)
        {
            if (target == null)
            {
                return 0;
            }

            foreach (var pair in _nodeById)
            {
                if (pair.Value.Target == target)
                {
                    return pair.Key;
                }
            }

            return 0;
        }

        public WidgetTreeSnapshot.Node NodeFor(int id) =>
            _nodeById.TryGetValue(id, out var node) ? node : null;

        protected override TreeViewItem BuildRoot()
        {
            _nodeById.Clear();

            var root = new TreeViewItem
            {
                id = 0,
                depth = -1,
                displayName = "Root",
            };

            if (_snapshot != null)
            {
                for (var i = 0; i < _snapshot.Roots.Count; i++)
                {
                    AddNode(root, _snapshot.Roots[i], i.ToString(), depth: 0);
                }
            }

            // TreeView will not tolerate a root with no children, and an empty scene is a normal
            // state rather than an error.
            if (!root.hasChildren)
            {
                root.AddChild(
                    new TreeViewItem
                    {
                        id = -1,
                        depth = 0,
                        displayName = "<no live widget tree>",
                    }
                );
            }

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        private void AddNode(
            TreeViewItem parent,
            WidgetTreeSnapshot.Node node,
            string path,
            int depth
        )
        {
            if (this.IssuesOnly && !node.SubtreeHasIssue)
            {
                return;
            }

            var id = IdFor(path);
            _nodeById[id] = node;

            var label = node.Index >= 0 ? $"[{node.Index}] {node.Label}" : node.Label;
            var item = new TreeViewItem
            {
                id = id,
                depth = depth,
                displayName = label,
            };
            parent.AddChild(item);

            for (var i = 0; i < node.Children.Count; i++)
            {
                AddNode(item, node.Children[i], path + "/" + i, depth + 1);
            }
        }

        private int IdFor(string path)
        {
            if (_idByPath.TryGetValue(path, out var id))
            {
                return id;
            }

            id = _nextId++;
            _idByPath[path] = id;
            return id;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var node = NodeFor(args.item.id);
            if (node == null)
            {
                base.RowGUI(args);
                return;
            }

            if (node.HasIssue)
            {
                EditorGUI.DrawRect(args.rowRect, new Color(0.85f, 0.65f, 0.1f, 0.18f));
            }

            for (var i = 0; i < args.GetNumVisibleColumns(); i++)
            {
                DrawCell(args, node, args.GetCellRect(i), (Column)args.GetColumn(i));
            }
        }

        private void DrawCell(
            RowGUIArgs args,
            WidgetTreeSnapshot.Node node,
            Rect cell,
            Column column
        )
        {
            switch (column)
            {
                case Column.Widget:
                    var indent = GetContentIndent(args.item);
                    cell.xMin += indent;

                    // A badge on an ancestor is what makes a collapsed branch navigable: it says which
                    // one is worth opening, rather than leaving you to expand everything and read.
                    if (node.SubtreeHasIssue)
                    {
                        var badge = new Rect(cell.x, cell.y + 5f, 7f, 7f);
                        EditorGUI.DrawRect(
                            badge,
                            node.HasIssue
                                ? new Color(0.95f, 0.6f, 0.1f)
                                : new Color(0.6f, 0.45f, 0.15f)
                        );
                    }

                    cell.xMin += 11f;

                    // Bold marks a widget with a GameObject behind it, which is also the set of rows
                    // that can be selected in the scene.
                    GUI.Label(
                        cell,
                        args.item.displayName,
                        node.Target == null ? EditorStyles.label : EditorStyles.boldLabel
                    );
                    break;

                case Column.Constraints:
                    GUI.Label(cell, node.Constraints ?? string.Empty, EditorStyles.miniLabel);
                    break;

                case Column.Size:
                    GUI.Label(cell, node.Size ?? string.Empty, EditorStyles.miniLabel);
                    break;
            }
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            var node = selectedIds is { Count: > 0 } ? NodeFor(selectedIds[0]) : null;
            this.NodeSelected?.Invoke(node);
        }

        protected override bool CanMultiSelect(TreeViewItem item) => false;
    }
}
