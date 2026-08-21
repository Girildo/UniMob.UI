using System;
using System.Collections.Generic;
using UniMob.UI.Rendering;

namespace UniMob.UI.Widgets
{
    /// <summary>
    ///     A layer of independently-lived entries, drawn over <see cref="Child"/>. Content declared
    ///     deep inside that child reaches the layer through <see cref="Of(BuildContext)"/>, and what it
    ///     puts there escapes its declaring parent's clip.
    /// </summary>
    /// <remarks>
    ///     A <c>Navigator</c> with the stack replaced by a set and the modality moved onto the entry.
    ///     Entries are ordered only by insertion, each is removable on its own, and the layer installs
    ///     no raycast target of its own: an entry that paints an anchored child and nothing else
    ///     catches no pointer elsewhere on the layer. An entry that means to block what is underneath
    ///     includes its own full-bleed <c>GestureDetector</c>.
    ///     <para>
    ///         Mount one high enough that its entries escape whatever they need to escape, and below
    ///         whatever they must inherit: an entry resolves theme, localization and DI from the
    ///         overlay's own position in the tree, not from the widget that inserted it.
    ///     </para>
    ///     <para>
    ///         Deliberately no <c>Canvas</c> of its own. Z-order comes from sibling index, which is
    ///         enough, and a nested canvas would put tap coordinates and widget geometry in different
    ///         spaces.
    ///     </para>
    /// </remarks>
    public class Overlay : StatefulWidget
    {
        /// <summary>
        ///     What the layer is drawn over. Every entry paints above it, and it is what makes
        ///     <see cref="Of(BuildContext)"/> reachable: content deep inside the child finds the layer
        ///     by walking up, the same way it finds a <c>Navigator</c>.
        /// </summary>
        public Widget? Child { get; init; }

        public override State CreateState() => new OverlayState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state) =>
            new RenderOverlay((IOverlayState)state);

        /// <summary>
        ///     The overlay above <paramref name="context"/>. Throws where there is none; use
        ///     <see cref="OfOrNull"/> where its absence is an answer rather than a fault.
        /// </summary>
        public static OverlayState Of(BuildContext context)
        {
            return OfOrNull(context)
                ?? throw new Exception(
                    "Overlay operation requested with a context that does not include an Overlay.\n"
                        + "The context used to insert an entry must be that of a widget that is a "
                        + "descendant of an Overlay widget."
                );
        }

        /// <summary>
        ///     The overlay above <paramref name="context"/>, or null where there is none.
        /// </summary>
        public static OverlayState? OfOrNull(BuildContext context) =>
            context.AncestorStateOfType<OverlayState>();
    }

    public class OverlayState : ViewState<Overlay>, IOverlayState
    {
        private readonly OverlayEntryList _entries = new OverlayEntryList();
        private readonly StateCollectionHolder _children;

        // Reused rather than rebuilt, as the navigator's stack does with its own list.
        private readonly List<Widget> _composed = new List<Widget>();

        public OverlayState()
        {
            _children = CreateChildren(_ => Compose());
        }

        public override WidgetViewReference View { get; } =
            WidgetViewReference.Registered("UniMob.MultiChildLayoutView");

        public IState[] Children => _children.Value;

        /// <summary>How many entries are on the layer. Untracked.</summary>
        public int Count => _entries.Count;

        /// <summary>
        ///     Adds <paramref name="build"/> to the top of the layer and hands back the entry that owns
        ///     it. The builder is re-invoked whenever the entry rebuilds, so an entry that reads atoms
        ///     stays live.
        /// </summary>
        /// <param name="context">
        ///     The inserter's context. Two things ride on it: the entry is removed when the state that
        ///     owns the context is disposed, and the builder is invoked with it, so what the builder
        ///     resolves in its own closure resolves against the declaring scope. That covers the
        ///     closure only -- descendants build against their own contexts, which sit under this
        ///     overlay.
        /// </param>
        /// <param name="onDismissed">
        ///     Called when the entry is taken off the layer by anything other than its owner: its own
        ///     chrome through <see cref="OverlayEntry.Dismiss"/>, or the layer through
        ///     <see cref="Clear"/>. Not called by <see cref="OverlayEntry.Remove"/>, where the owner
        ///     already knows, and not called when the inserter dies, where there is nobody left to tell.
        /// </param>
        public OverlayEntry Insert(
            BuildContext context,
            WidgetBuilder<Widget> build,
            Action? onDismissed = null
        )
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (build == null)
                throw new ArgumentNullException(nameof(build));

            var owner =
                context.State
                ?? throw new ArgumentException(
                    "An overlay entry is owned by the state that inserted it, so it needs that "
                        + "state's context rather than a root one.",
                    nameof(context)
                );

            if (owner.StateLifetime.IsDisposed)
            {
                throw new InvalidOperationException(
                    "Cannot insert an overlay entry from a disposed state: the entry would outlive "
                        + "its owner and nothing would take it off the layer."
                );
            }

            var entry = new OverlayEntry(this, onDismissed);

            // Keyed on the entry, so inserting or removing one leaves its siblings alone:
            // reconciliation matches by key and runtime type, and without a stable key per entry a
            // live sibling would be torn down and rebuilt in place of the entry that actually left.
            _entries.Add(entry, new Builder(_ => build(context)) { Key = Key.Of(entry) });

            owner.StateLifetime.Register(entry.Remove);

            return entry;
        }

        /// <summary>
        ///     Takes every entry off the layer, dismissing each. What a host calls when the thing the
        ///     entries annotate is no longer what the user is looking at.
        /// </summary>
        public void Clear()
        {
            foreach (var entry in _entries.Snapshot())
            {
                Withdraw(entry, dismissed: true);
            }
        }

        /// <summary>
        ///     Ends every entry still on the layer before the state tree takes them apart. Silent: an
        ///     unmount is not a dismissal, and whoever would be told is being disposed alongside it.
        /// </summary>
        public override void Dispose()
        {
            foreach (var entry in _entries.Snapshot())
            {
                _entries.Remove(entry);
                entry.MarkRemoved();
            }

            base.Dispose();
        }

        public override string GetDiagnosticInfo()
        {
            var count = _entries.Count;

            if (count == 0)
                return "empty";

            return count == 1 ? "1 entry" : count + " entries";
        }

        /// <summary>
        ///     The child first, then the entries in insertion order.
        /// </summary>
        /// <remarks>
        ///     The child holds position zero for as long as the overlay lives, which is what lets it go
        ///     unkeyed: reconciliation matches an unkeyed child only while it stays where it was, and
        ///     everything that moves here is keyed on its entry.
        /// </remarks>
        private List<Widget> Compose()
        {
            _composed.Clear();

            if (Widget.Child != null)
            {
                _composed.Add(Widget.Child);
            }

            _composed.AddRange(_entries.Widgets);

            return _composed;
        }

        internal void Withdraw(OverlayEntry entry, bool dismissed)
        {
            if (!_entries.Remove(entry))
            {
                return;
            }

            entry.MarkRemoved();

            if (dismissed)
            {
                // NoWatch, because a dismissal handler is ordinary imperative code that can be reached
                // from inside a reconciliation, and must not gain dependencies on whatever computation
                // happens to be running.
                using (Atom.NoWatch)
                {
                    entry.NotifyDismissed();
                }
            }
        }
    }

    /// <summary>
    ///     The entries on a layer and the widgets built from them, kept in step and versioned as one.
    /// </summary>
    /// <remarks>
    ///     The version is written rather than incremented in place: <c>_version.Value++</c> is a read
    ///     as well as a write, so a list mutated from inside a computation would subscribe that
    ///     computation and immediately obsolete it.
    /// </remarks>
    internal sealed class OverlayEntryList
    {
        private readonly List<OverlayEntry> _entries = new List<OverlayEntry>();
        private readonly List<Widget> _widgets = new List<Widget>();
        private readonly MutableAtom<int> _version = Atom.Value(int.MinValue);

        private int _revision = int.MinValue;

        public int Count => _entries.Count;

        public List<Widget> Widgets
        {
            get
            {
                _version.Get();
                return _widgets;
            }
        }

        public void Add(OverlayEntry entry, Widget widget)
        {
            _entries.Add(entry);
            _widgets.Add(widget);
            _version.Value = ++_revision;
        }

        public bool Remove(OverlayEntry entry)
        {
            var index = _entries.IndexOf(entry);
            if (index < 0)
            {
                return false;
            }

            _entries.RemoveAt(index);
            _widgets.RemoveAt(index);
            _version.Value = ++_revision;
            return true;
        }

        /// <summary>
        ///     The entries as they stand, bottom-most first. Untracked, and a copy, so it is safe to
        ///     mutate the list while walking it.
        /// </summary>
        public OverlayEntry[] Snapshot() => _entries.ToArray();
    }
}
