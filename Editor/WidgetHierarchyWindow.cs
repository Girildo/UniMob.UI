using UniMob.UI.Diagnostics;
using UniMob.UI.Widgets;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
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
        [SerializeField]
        private TreeViewState _treeState;

        [SerializeField]
        private MultiColumnHeaderState _headerState;

        private WidgetTreeView _tree;
        private SearchField _search;

        /// <summary>
        ///     A guard against runaway recursion, not a display choice. Real pages already reach the
        ///     mid-twenties, so a ceiling anywhere near that truncates them instead of protecting
        ///     anything.
        /// </summary>
        private int _maxDepth = 100;

        private bool _autoRefresh = true;
        private double _nextRefreshAt;

        /// <summary>
        ///     Set while this window is the one changing <see cref="Selection"/>, so the change it
        ///     causes does not come back around and re-select what the user just clicked.
        /// </summary>
        private bool _drivingSelection;

        [MenuItem("Window/UniMob/Widget Hierarchy")]
        private static void Open()
        {
            var window = GetWindow<WidgetHierarchyWindow>();
            window.titleContent = new GUIContent("Widgets");
            window.Show();
        }

        private void OnEnable()
        {
            _treeState ??= new TreeViewState();

            var freshHeader = WidgetTreeView.CreateHeaderState();
            if (
                _headerState != null
                && MultiColumnHeaderState.CanOverwriteSerializedFields(_headerState, freshHeader)
            )
            {
                // Carries the user's column widths across a domain reload, but only when the columns
                // are still the same ones.
                MultiColumnHeaderState.OverwriteSerializedFields(_headerState, freshHeader);
            }

            _headerState = freshHeader;

            _tree = new WidgetTreeView(_treeState, new MultiColumnHeader(_headerState));
            _tree.NodeSelected += OnNodeSelected;
            _search = new SearchField();

            Selection.selectionChanged += OnSceneSelectionChanged;

            Refresh();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSceneSelectionChanged;

            if (_tree != null)
            {
                _tree.NodeSelected -= OnNodeSelected;
            }
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

        private void Refresh() => _tree?.SetSnapshot(WidgetTreeSnapshot.Take(_maxDepth));

        private void OnGUI()
        {
            DrawToolbar();

            var rect = GUILayoutUtility.GetRect(
                0,
                100000,
                0,
                100000,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true)
            );
            _tree.OnGUI(rect);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(58)))
                {
                    Refresh();
                }

                _autoRefresh = GUILayout.Toggle(
                    _autoRefresh,
                    "Auto",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(42)
                );

                var issuesOnly = GUILayout.Toggle(
                    _tree.IssuesOnly,
                    "Issues only",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(74)
                );

                if (issuesOnly != _tree.IssuesOnly)
                {
                    _tree.IssuesOnly = issuesOnly;
                    Refresh();
                }

                if (GUILayout.Button("Expand", EditorStyles.toolbarButton, GUILayout.Width(54)))
                {
                    _tree.ExpandAll();
                }

                if (GUILayout.Button("Collapse", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    _tree.CollapseAll();
                }

                GUILayout.Space(6);
                _tree.searchString = _search.OnToolbarGUI(_tree.searchString);

                GUILayout.FlexibleSpace();

                // The text dump, free: same walk, same node renderer, pasteable into a bug report.
                if (GUILayout.Button("Copy tree", EditorStyles.toolbarButton, GUILayout.Width(68)))
                {
                    CopyTreeToClipboard();
                }
            }
        }

        /// <summary>Window to scene.</summary>
        /// <remarks>
        ///     A build-only widget has no GameObject, and silently selecting the nearest descendant that
        ///     does would point at something the user did not click. Better to say so and select
        ///     nothing.
        /// </remarks>
        private void OnNodeSelected(WidgetTreeSnapshot.Node node)
        {
            if (node?.Target == null)
            {
                if (node != null)
                {
                    ShowNotification(
                        new GUIContent("Build-only widget: no GameObject to select."),
                        0.7f
                    );
                }

                return;
            }

            _drivingSelection = true;
            try
            {
                Selection.activeGameObject = node.Target;
                EditorGUIUtility.PingObject(node.Target);
            }
            finally
            {
                _drivingSelection = false;
            }
        }

        /// <summary>Scene or Hierarchy to window.</summary>
        /// <remarks>
        ///     Walks up from the selected GameObject to the nearest one this tree knows about. Landing
        ///     on the nearest view-backed ancestor is the point rather than a shortcoming: the
        ///     build-only widgets around it are then visible in this window, which is the only place
        ///     they ever are.
        /// </remarks>
        private void OnSceneSelectionChanged()
        {
            if (_drivingSelection || _tree == null)
            {
                return;
            }

            var selected = Selection.activeGameObject;

            while (selected != null)
            {
                var id = _tree.FindIdForGameObject(selected);
                if (id != 0)
                {
                    // Deliberately without FireSelectionChanged. Firing it would run the window-to-scene
                    // handler, which would then write back the ancestor we walked up to -- quietly
                    // moving the user's selection off whatever they actually clicked.
                    _tree.SetSelection(new[] { id }, TreeViewSelectionOptions.RevealAndFrame);
                    Repaint();
                    return;
                }

                var parent = selected.transform.parent;
                selected = parent == null ? null : parent.gameObject;
            }
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
                    if (panel.LayoutRoot != null)
                    {
                        text += LayoutTree.Describe(panel.LayoutRoot, _maxDepth);
                    }
                }
            }

            EditorGUIUtility.systemCopyBuffer = text;
            ShowNotification(new GUIContent("Layout tree copied."), 0.7f);
        }
    }
}
