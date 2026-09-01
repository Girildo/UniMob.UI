using UniMob.UI.Diagnostics;
using UniMob.UI.Widgets;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace UniMob.UI.Editor
{
    /// <summary>
    ///     The widget tree, as the author wrote it.
    /// </summary>
    /// <remarks>
    ///     Unity's Hierarchy shows GameObjects, and a GameObject exists only where a state has a view.
    ///     Every build-only state is therefore invisible in it -- <c>HocState</c> and
    ///     <c>StatelessElement</c>, which have no view at all and forward <c>InnerViewState</c> to the
    ///     child they build, so every <c>StatelessWidget</c> and every <c>ProxyWidget</c> is missing.
    ///     Those are disproportionately the widgets that <i>cause</i> layout faults, and the ones you
    ///     can see are named "Column[MultiChildLayoutView]" rather than by what the author wrote.
    ///     <para>
    ///         Owning a <c>RenderProxy</c> is not the test for it. Flexible, Expanded, Positioned,
    ///         Opacity, IgnorePointer, Clickable, GestureDetector and ConstrainedBuilder each own one
    ///         and each registers a view, so all of them do appear.
    ///     </para>
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
        [SerializeField]
        private int _maxDepth = 100;

        private bool _autoRefresh = true;
        private double _nextRefreshAt;

        /// <summary>
        ///     Hit test by layout box rather than by what paints. A toggle rather than only the Alt
        ///     shortcut, because a held modifier is not something anyone discovers -- and the widgets
        ///     this reaches are exactly the ones a newcomer will be confused about failing to click.
        /// </summary>
        private bool _geometric;

        /// <summary>Alt does the same thing without leaving the pointer, for whoever already knows.</summary>
        private bool Geometric => _geometric || WidgetPicker.IsAltHeld;

        /// <summary>Last drawn Alt state, so the toolbar can be repainted the moment it changes.</summary>
        private bool _altWasHeld;

        /// <summary>
        ///     Set while this window is the one changing <see cref="Selection"/>, so the change it
        ///     causes does not come back around and re-select what the user just clicked.
        /// </summary>
        private bool _drivingSelection;

        /// <summary>
        ///     What the pointer was last over, so a move that stays on the same widget costs nothing.
        /// </summary>
        /// <remarks>
        ///     Compared by reference, and the snapshot is rebuilt twice a second, so this stops matching
        ///     after a refresh and the next move re-syncs. That is the intent: a stale node is not the
        ///     row to leave selected.
        /// </remarks>
        private WidgetTreeSnapshot.Node _hovered;

        private const string ToggleShortcutId = "UniMob/Toggle Widget Picker";

        [MenuItem("Window/UniMob/Widget Hierarchy")]
        private static void Open()
        {
            var window = GetWindow<WidgetHierarchyWindow>();
            window.titleContent = new GUIContent("Widgets");
            window.Show();
        }

        /// <summary>
        ///     Opens the window and starts picking, the way a browser's inspect shortcut does.
        /// </summary>
        /// <remarks>
        ///     Registered through <see cref="ShortcutAttribute"/> rather than as a menu accelerator, so
        ///     it is listed in Edit &gt; Shortcuts and can be rebound. That matters more than the default
        ///     chosen here: <c>Ctrl/Cmd+Shift+C</c> is the one every web developer already has in their
        ///     fingers, and it is also close to Unity's own bindings, so a profile that already uses it
        ///     will show the clash there and take one click to settle.
        ///     <para>
        ///         Global rather than scoped to this window. Scoping it would be tidier, but the moment
        ///         you want to inspect something is the moment after clicking around the app, when focus
        ///         is on the Game view -- a shortcut that works only sometimes is worse than none.
        ///     </para>
        /// </remarks>
        [Shortcut(ToggleShortcutId, KeyCode.C, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        private static void TogglePicking()
        {
            var window = GetWindow<WidgetHierarchyWindow>();
            window.titleContent = new GUIContent("Widgets");
            window.Show();

            // The picker works by putting a click-catcher into the running scene, so there is nothing
            // to arm outside play mode. Say so rather than appearing to do nothing.
            if (!EditorApplication.isPlaying)
            {
                window.ShowNotification(new GUIContent("Enter play mode to pick a widget."), 0.7f);
                window.Repaint();
                return;
            }

            if (WidgetPicker.IsPicking)
            {
                WidgetPicker.Disarm();
            }
            else
            {
                WidgetPicker.Arm();
            }

            window.Repaint();
        }

        /// <summary>
        ///     The Select button's label, carrying whatever the shortcut is currently bound to.
        /// </summary>
        /// <remarks>
        ///     Read from <see cref="ShortcutManager"/> rather than written out, so a rebound shortcut is
        ///     described correctly instead of the tooltip quietly lying. Built once: a rebind is rare
        ///     and reopening the window picks it up.
        /// </remarks>
        private GUIContent _selectLabel;

        private static GUIContent BuildSelectLabel()
        {
            var tooltip = "Click a widget in the Game view to select it here.";

            var binding = ShortcutManager.instance.GetShortcutBinding(ToggleShortcutId).ToString();
            if (!string.IsNullOrEmpty(binding))
            {
                tooltip +=
                    $"\n\n{binding} toggles this from anywhere; rebind it in Edit > Shortcuts.";
            }

            return new GUIContent("Select", tooltip);
        }

        private void OnEnable()
        {
            _treeState ??= new TreeViewState();
            _selectLabel = BuildSelectLabel();

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
            WidgetPicker.Picked += OnPicked;
            WidgetPicker.Hovered += OnHovered;
            WidgetPicker.Exited += OnPointerLeftApp;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            Refresh();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSceneSelectionChanged;
            WidgetPicker.Picked -= OnPicked;
            WidgetPicker.Hovered -= OnHovered;
            WidgetPicker.Exited -= OnPointerLeftApp;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;

            // A picker outliving the window that armed it would eat every click in the Game view with
            // nothing left listening to explain why.
            WidgetPicker.Disarm();

            if (_tree != null)
            {
                _tree.NodeSelected -= OnNodeSelected;
            }
        }

        private void OnPlayModeChanged(PlayModeStateChange change) => WidgetPicker.Disarm();

        /// <summary>
        ///     This window has the pointer, so the app does not. Without this the hover latch inside the
        ///     picker never lowers -- see <see cref="WidgetPicker.ReleasePointer"/> -- and every row you
        ///     click here is ignored because hover still owns the highlight.
        /// </summary>
        private void OnFocus()
        {
            WidgetPicker.ReleasePointer();
            _hovered = null;
            HighlightSelection();
        }

        private void Update()
        {
            // Alt is a mode, and a mode the toolbar shows has to be shown while it is held rather than
            // at the next refresh -- half a second of the toggle disagreeing with the outline is worse
            // than not showing it at all. Only while picking, so this is not polling input at idle.
            if (WidgetPicker.IsPicking)
            {
                var alt = WidgetPicker.IsAltHeld;
                if (alt != _altWasHeld)
                {
                    _altWasHeld = alt;
                    Repaint();
                }
            }

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

        private void Refresh()
        {
            _tree?.SetSnapshot(WidgetTreeSnapshot.Take(_maxDepth));

            // Re-applied on every refresh rather than only when the selection changes, so the
            // highlight follows a widget that moves -- a list scrolling, an animation, a relayout --
            // instead of being left where the widget used to be.
            HighlightSelection();
        }

        /// <summary>
        ///     The pointer left the app, so hover stops owning the highlight and the selection takes it
        ///     back. Forgetting what was hovered matters: coming back in over the same widget has to
        ///     re-light it, and a stale match would leave the highlight wherever the selection put it.
        /// </summary>
        private void OnPointerLeftApp()
        {
            _hovered = null;
            HighlightSelection();
        }

        /// <summary>
        ///     Lights the selected widget, unless the pointer is in the app and owns the highlight.
        /// </summary>
        private void HighlightSelection()
        {
            if (!WidgetPicker.IsArmed || WidgetPicker.IsPointerOverApp)
            {
                return;
            }

            var selection = _tree.GetSelection();
            var node = selection.Count > 0 ? _tree.NodeFor(selection[0]) : null;
            var rect = WidgetTreeSnapshot.ScreenRectOf(node);

            if (rect.width > 0f || rect.height > 0f)
            {
                WidgetPicker.SetHighlight(rect);
            }
            else
            {
                WidgetPicker.ClearHighlight();
            }
        }

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
                // Inspect mode. Only meaningful while the app is running, since it works by putting a
                // click-catcher into the live scene.
                using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                {
                    var armed = GUILayout.Toggle(
                        WidgetPicker.IsPicking,
                        _selectLabel,
                        EditorStyles.toolbarButton,
                        GUILayout.Width(52)
                    );

                    if (armed != WidgetPicker.IsPicking)
                    {
                        if (armed)
                        {
                            WidgetPicker.Arm();
                        }
                        else
                        {
                            WidgetPicker.Disarm();
                        }
                    }

                    // Shown pressed and disabled while Alt forces it: the window must not claim a mode
                    // the picker is not in, and seeing the button move is how the shortcut teaches
                    // itself to whoever found the button first.
                    var altHeld = WidgetPicker.IsAltHeld;

                    using (new EditorGUI.DisabledScope(altHeld))
                    {
                        var geometric = GUILayout.Toggle(
                            _geometric || altHeld,
                            new GUIContent(
                                "Boxes",
                                "Pick by layout box instead of by what paints.\n\n"
                                    + "Select normally asks what a click would hit, so it cannot reach "
                                    + "a widget that takes no input: anything under an IgnorePointer, a "
                                    + "CustomPaint, or any image with raycastTarget off. This reaches "
                                    + "those, at the cost of picking things you cannot see.\n\n"
                                    + "Hold Alt for the same thing without leaving the pointer. The "
                                    + "outline turns violet whenever boxes are deciding."
                            ),
                            EditorStyles.toolbarButton,
                            GUILayout.Width(48)
                        );

                        // Never while Alt is driving it, or releasing Alt would leave the mode stuck on.
                        if (!altHeld)
                        {
                            _geometric = geometric;
                        }
                    }
                }

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
                GUILayout.Label("Depth", EditorStyles.miniLabel, GUILayout.Width(36));

                var depth = EditorGUILayout.IntSlider(_maxDepth, 4, 400, GUILayout.Width(130));
                if (depth != _maxDepth)
                {
                    _maxDepth = depth;
                    Refresh();
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
            // Immediately, rather than waiting for the next refresh tick: a selection that lights up
            // half a second later reads as not working.
            HighlightSelection();

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

        /// <summary>
        ///     Lights the widget under the pointer while inspect mode is on, and brings the row with it.
        /// </summary>
        /// <remarks>
        ///     The row moving is what makes sweeping the app readable: a rectangle changing size says
        ///     almost nothing on its own, and without it the window sits still until you commit to a
        ///     click. Selecting rather than merely scrolling, because the selected row is the one the
        ///     eye finds.
        ///     <para>
        ///         Hit tested against the snapshot already on screen rather than a fresh one. This fires
        ///         on every pointer move inside a running app, and re-walking the whole tree at that rate
        ///         would make the thing it is inspecting stutter. The timer refresh is what keeps it
        ///         current; being at most half a second stale is invisible for a highlight and would not
        ///         be for a frame rate.
        ///     </para>
        /// </remarks>
        private void OnHovered(Vector2 screenPoint)
        {
            var hit = _tree?.Snapshot?.HitTest(screenPoint, this.Geometric);

            // Most moves land on the widget already lit. Doing the work anyway would re-frame the tree
            // and repaint the window at pointer-move rate for no change on screen.
            if (ReferenceEquals(hit, _hovered))
            {
                return;
            }

            _hovered = hit;

            if (hit == null)
            {
                WidgetPicker.ClearHighlight();
                return;
            }

            WidgetPicker.SetHighlight(WidgetTreeSnapshot.ScreenRectOf(hit), this.Geometric);

            var id = _tree.FindIdForNode(hit);
            if (id == 0)
            {
                return;
            }

            // Deliberately without FireSelectionChanged. That runs the window-to-scene handler, which
            // writes Selection.activeGameObject and pings it -- fine once per click, unbearable once
            // per mouse move.
            _tree.SetSelection(new[] { id }, TreeViewSelectionOptions.RevealAndFrame);
            Repaint();
        }

        /// <summary>Game view to window: a click in the running UI selects the widget under it.</summary>
        /// <remarks>
        ///     Re-snapshots first. Inspect mode is most useful on something that just changed, and the
        ///     hit test has to run against the geometry as it is now rather than as it was up to half a
        ///     second ago.
        /// </remarks>
        private void OnPicked(Vector2 screenPoint)
        {
            Refresh();

            var snapshot = _tree?.Snapshot;
            var hit = snapshot?.HitTest(screenPoint, this.Geometric);

            if (hit == null)
            {
                ShowNotification(new GUIContent("No widget under that point."), 0.7f);
                Repaint();
                return;
            }

            // The hunt ends here, as it does in a browser's element picker: the app gets its input
            // back and the dim goes, while the outline stays on what was found. Before selecting,
            // because StopPicking also lowers the hover latch that would otherwise suppress it.
            WidgetPicker.StopPicking();
            _hovered = null;

            var id = _tree.FindIdForNode(hit);
            if (id != 0)
            {
                _tree.SetSelection(new[] { id }, TreeViewSelectionOptions.RevealAndFrame);
            }

            OnNodeSelected(hit);
            Focus();
            Repaint();
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
