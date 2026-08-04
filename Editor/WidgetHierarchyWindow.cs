using System.Collections.Generic;
using UniMob.UI.Diagnostics;
using UniMob.UI.Widgets;
using UnityEditor;
using UnityEngine;

namespace UniMob.UI.Editor
{
    /// <summary>
    ///     The widget tree, as the author wrote it.
    /// </summary>
    /// <remarks>
    ///     Unity's Hierarchy shows GameObjects, and a GameObject exists only where a state has a view.
    ///     Every build-only state is therefore invisible in it -- <c>HocState</c>,
    ///     <c>StatelessElement</c>, and everything owning a <c>RenderProxy</c>: Flexible, Expanded,
    ///     Positioned, Opacity, IgnorePointer, Clickable, GestureDetector, ConstrainedBuilder. Those are
    ///     disproportionately the widgets that <i>cause</i> layout faults, and the ones you can see are
    ///     named "Column[MultiChildLayoutView]" rather than by what the author wrote.
    ///     <para>
    ///         Rows come from <c>DiagnosticNode</c> and badges from <c>RenderObject.HasLayoutIssue</c>,
    ///         so this window agrees with the console by construction rather than by maintenance.
    ///     </para>
    /// </remarks>
    public sealed class WidgetHierarchyWindow : EditorWindow
    {
        private const float RowHeight = 18f;
        private const float IndentWidth = 14f;

        private WidgetTreeSnapshot _snapshot;
        private Vector2 _scroll;
        private int _maxDepth = 24;
        private bool _autoRefresh = true;
        private bool _issuesOnly;
        private double _nextRefreshAt;

        /// <summary>
        ///     Collapsed state, keyed by path rather than by node, because every refresh builds new
        ///     nodes. Keying on the objects themselves would silently reset the tree on every tick.
        /// </summary>
        private readonly HashSet<string> _collapsed = new HashSet<string>();

        /// <summary>Keyed by path for the same reason <see cref="_collapsed"/> is.</summary>
        private string _selectedPath;

        private bool _rowIsOdd;

        [MenuItem("Window/UniMob/Widget Hierarchy")]
        private static void Open()
        {
            var window = GetWindow<WidgetHierarchyWindow>();
            window.titleContent = new GUIContent("Widgets");
            window.Show();
        }

        private void OnEnable()
        {
            // Hover has to track the pointer, and without this the window only hears about the mouse
            // when something else already caused a repaint.
            this.wantsMouseMove = true;
            Refresh();
        }

        private void Update()
        {
            // Polled on a timer rather than driven by a reaction. An Atom.Reaction from an editor
            // window would make the window a participant in the app's dependency graph, which is the
            // one thing a debugging tool must never become.
            if (!_autoRefresh || EditorApplication.timeSinceStartup < _nextRefreshAt)
            {
                return;
            }

            _nextRefreshAt = EditorApplication.timeSinceStartup + 0.5f;
            Refresh();
            Repaint();
        }

        private void Refresh() => _snapshot = WidgetTreeSnapshot.Take(_maxDepth);

        private void OnGUI()
        {
            DrawToolbar();

            if (_snapshot == null || _snapshot.Roots.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No live widget tree. Enter play mode, or open a scene containing a ViewPanel.",
                    MessageType.Info
                );
                return;
            }

            if (Event.current.type == EventType.MouseMove)
            {
                Repaint();
            }

            _rowIsOdd = false;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (var i = 0; i < _snapshot.Roots.Count; i++)
            {
                DrawNode(_snapshot.Roots[i], depth: 0, path: i.ToString());
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    Refresh();
                }

                _autoRefresh = GUILayout.Toggle(
                    _autoRefresh,
                    "Auto",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(44)
                );

                _issuesOnly = GUILayout.Toggle(
                    _issuesOnly,
                    "Issues only",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(78)
                );

                GUILayout.Space(8);
                GUILayout.Label("Depth", EditorStyles.miniLabel, GUILayout.Width(38));
                _maxDepth = EditorGUILayout.IntSlider(_maxDepth, 2, 40, GUILayout.Width(140));

                GUILayout.FlexibleSpace();

                // The text dump, free: same walk, same node renderer, pasteable into a bug report.
                if (GUILayout.Button("Copy tree", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    CopyTreeToClipboard();
                }

                GUILayout.Label(
                    _snapshot == null ? string.Empty : $"{_snapshot.NodeCount} nodes",
                    EditorStyles.miniLabel
                );
            }
        }

        private void DrawNode(WidgetTreeSnapshot.Node node, int depth, string path)
        {
            if (_issuesOnly && !node.SubtreeHasIssue)
            {
                return;
            }

            var collapsed = _collapsed.Contains(path);
            var row = GUILayoutUtility.GetRect(0, RowHeight, GUILayout.ExpandWidth(true));

            // Backgrounds, weakest to strongest, so the stronger signal always wins the row. Without
            // any of these a deep tree is a wall of text at forty indentation levels and the eye has
            // nothing to track along a line.
            if (_rowIsOdd)
            {
                EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.025f));
            }

            if (node.HasIssue)
            {
                EditorGUI.DrawRect(row, new Color(0.85f, 0.65f, 0.1f, 0.20f));
            }

            if (row.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.05f));
            }

            if (path == _selectedPath)
            {
                EditorGUI.DrawRect(row, new Color(0.24f, 0.48f, 0.90f, 0.35f));
            }

            _rowIsOdd = !_rowIsOdd;

            var x = row.x + depth * IndentWidth;

            if (node.Children.Count > 0)
            {
                var foldout = new Rect(x, row.y, 14f, row.height);
                if (GUI.Button(foldout, collapsed ? "▸" : "▾", EditorStyles.label))
                {
                    if (!_collapsed.Remove(path))
                    {
                        _collapsed.Add(path);
                    }
                }
            }

            x += 16f;

            // The badge on an ancestor is what makes a collapsed tree navigable: it says which branch
            // is worth opening, instead of leaving you to expand everything and read.
            if (node.SubtreeHasIssue)
            {
                var badge = new Rect(x, row.y + 4f, 8f, 8f);
                EditorGUI.DrawRect(
                    badge,
                    node.HasIssue ? new Color(0.9f, 0.55f, 0.1f) : new Color(0.6f, 0.45f, 0.15f)
                );
            }

            x += 12f;

            var label = node.Index >= 0 ? $"[{node.Index}] {node.Label}" : node.Label;
            var numbers = string.IsNullOrEmpty(node.Size)
                ? string.Empty
                : $"{node.Constraints}  ->  {node.Size}";

            var numbersWidth = Mathf.Min(340f, row.width * 0.45f);
            var labelRect = new Rect(x, row.y, row.width - x - numbersWidth, row.height);
            var numbersRect = new Rect(row.xMax - numbersWidth, row.y, numbersWidth, row.height);

            var style = node.Target == null ? EditorStyles.label : EditorStyles.boldLabel;
            GUI.Label(labelRect, label, style);
            GUI.Label(numbersRect, numbers, EditorStyles.miniLabel);

            // The whole row, not just the label. At this indentation the label is a narrow target in
            // the middle of a wide row, and everything left and right of it looks equally clickable.
            if (
                Event.current.type == EventType.MouseDown
                && row.Contains(Event.current.mousePosition)
            )
            {
                _selectedPath = path;
                Select(node);
                Event.current.Use();
                Repaint();
            }

            if (collapsed)
            {
                return;
            }

            for (var i = 0; i < node.Children.Count; i++)
            {
                DrawNode(node.Children[i], depth + 1, path + "/" + i);
            }
        }

        /// <summary>
        ///     Selects a node's GameObject, where it has one.
        /// </summary>
        /// <remarks>
        ///     A build-only widget has no GameObject, and silently selecting the nearest descendant that
        ///     does would point at something the user did not click. Better to say so.
        /// </remarks>
        private void Select(WidgetTreeSnapshot.Node node)
        {
            if (node.Target == null)
            {
                ShowNotification(
                    new GUIContent("Build-only widget: no GameObject to select."),
                    0.8f
                );
                return;
            }

            Selection.activeGameObject = node.Target;
            EditorGUIUtility.PingObject(node.Target);
        }

        private void CopyTreeToClipboard()
        {
            var text = string.Empty;

            using (Atom.NoWatch)
            {
                foreach (
                    var panel in FindObjectsByType<ViewPanel>(
                        FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None
                    )
                )
                {
                    text += LayoutTree.Describe(panel.LayoutRoot, _maxDepth);
                }
            }

            EditorGUIUtility.systemCopyBuffer = text;
            ShowNotification(new GUIContent("Layout tree copied."), 0.8f);
        }
    }
}
