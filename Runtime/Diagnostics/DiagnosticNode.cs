using System;
using System.Text;

namespace UniMob.UI.Diagnostics
{
    /// <summary>
    ///     Renders one node of the widget tree as text: what it is, which one it is, and whatever its
    ///     author had to say about it. <c>AppButton#submit "Cart Add to cart"</c>.
    /// </summary>
    /// <remarks>
    ///     The one place the label channels are resolved, and the primitive everything describing a
    ///     tree is built from -- the upward path in an error message, the on-demand downward dump, a
    ///     report's header, and <see cref="State"/>'s debugger display. Those were three inconsistent
    ///     walks and one that did not exist.
    ///     <para>
    ///         Every channel is guarded separately. A label is author code called at the worst possible
    ///         moment, so one that throws costs its own line and nothing else; a single catch around the
    ///         whole renderer would make a bug in here look like a bad label.
    ///     </para>
    /// </remarks>
    public static class DiagnosticNode
    {
        /// <summary>
        ///     Describes <paramref name="state"/> in one line. Never throws, and never registers an atom
        ///     dependency: the whole read is inside <c>Atom.NoWatch</c>, which is what lets a label be a
        ///     live value without dragging whoever is reporting into a dependency on it.
        /// </summary>
        public static string Describe(IState? state)
        {
            var builder = new StringBuilder();
            AppendTo(builder, state);
            return builder.ToString();
        }

        /// <summary>
        ///     Channel 0 alone: the widget's type, with no Key and no label. What a summary sentence
        ///     wants, where the full node would bury the fault it is describing.
        /// </summary>
        internal static string NameOf(IState? state)
        {
            return state is null ? "<null>" : TypeName(state);
        }

        internal static void AppendTo(StringBuilder builder, IState? state)
        {
            if (state is null)
            {
                builder.Append("<null>");
                return;
            }

            using (Atom.NoWatch)
            {
                builder.Append(TypeName(state));
                AppendKey(builder, state);
                AppendLabel(builder, state);
            }
        }

        // Channel 0: the widget's type, because that is the name the author wrote. Falls back to the
        // state's own type rather than reporting a failure -- there is always an answer here, and the
        // state type is a worse answer rather than no answer. The suffix trim is what makes that
        // fallback readable: ColumnState is a Column.
        private static string TypeName(IState state)
        {
            try
            {
                var widget = state.RawWidget;
                if (widget != null)
                {
                    return PrettyTypeName(widget.GetType());
                }
            }
            catch (Exception)
            {
                // A state can refuse to answer for its widget, and mid-construction it has none.
            }

            var stateName = PrettyTypeName(state.GetType());
            return stateName.Length > 5 && stateName.EndsWith("State", StringComparison.Ordinal)
                ? stateName.Substring(0, stateName.Length - "State".Length)
                : stateName;
        }

        // Channel 1: the Key, for a widget the author is merely composing rather than writing. Both
        // Key subclasses live in this assembly, so rendering them costs no new API -- and both of
        // their ToString()s are unusable here: "[Key: body]" and a fully qualified type name.
        private static void AppendKey(StringBuilder builder, IState state)
        {
            try
            {
                var key = state.Key;
                if (key is null)
                {
                    return;
                }

                builder.Append('#');

                switch (key)
                {
                    case ObjectKey objectKey:
                        builder.Append(objectKey.Value);
                        break;

                    // A widget already carrying a GlobalKey has spent this channel on functional
                    // identity and cannot be labelled here. Say so, rather than printing a type name
                    // that reads like a label.
                    case GlobalKey globalKey:
                        builder.Append("global");
                        var arguments = globalKey.GetType().GenericTypeArguments;
                        if (arguments.Length > 0)
                        {
                            builder.Append('<').Append(PrettyTypeName(arguments[0])).Append('>');
                        }

                        break;

                    default:
                        builder.Append(key);
                        break;
                }
            }
            catch (Exception ex)
            {
                builder.Append(" <key threw: ").Append(ex.GetType().Name).Append('>');
            }
        }

        /// <summary>
        ///     Cuts <paramref name="value"/> to <paramref name="max"/> characters, marking the cut.
        ///     What an author calls when their label echoes content they do not control.
        /// </summary>
        /// <remarks>
        ///     Length is the author's call, not this renderer's: the right cut differs per widget, and
        ///     a tight central cap would make two nodes sharing a prefix indistinguishable -- the exact
        ///     failure a label exists to prevent. <see cref="Sanitize"/> only backstops the pathological
        ///     case.
        /// </remarks>
        public static string? Truncate(string? value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max || max < 0)
            {
                return value;
            }

            return value.Substring(0, max) + "...";
        }

        // Channels 2 and 3: what the widget, or the state that beat it to it, says about itself.
        private static void AppendLabel(StringBuilder builder, IState state)
        {
            string label;
            try
            {
                label = state.GetDiagnosticInfo();
            }
            catch (Exception ex)
            {
                builder.Append(" <label threw: ").Append(ex.GetType().Name).Append('>');
                return;
            }

            if (!string.IsNullOrEmpty(label))
            {
                builder.Append(" \"").Append(Sanitize(label)).Append('"');
            }
        }

        // The half of the label contract that has no legitimate exception. A node is one line
        // everywhere it is read -- an ancestor chain, a console entry's first line, a tree row -- so a
        // newline is not a long label, it is a broken one. The length backstop sits far above any
        // deliberate label and catches only a widget echoing a paragraph of content it did not write.
        private const int MaxLabelLength = 120;

        private static string Sanitize(string label)
        {
            var truncated = Truncate(label, MaxLabelLength);

            StringBuilder cleaned = null;
            for (var i = 0; i < truncated.Length; i++)
            {
                var c = truncated[i];
                if (!char.IsControl(c))
                {
                    cleaned?.Append(c);
                    continue;
                }

                cleaned ??= new StringBuilder(truncated.Length).Append(truncated, 0, i);
                cleaned.Append(' ');
            }

            return cleaned?.ToString() ?? truncated;
        }

        private static string PrettyTypeName(Type type)
        {
            if (!type.IsGenericType)
            {
                return type.Name;
            }

            var name = type.Name;
            var arity = name.IndexOf('`');
            if (arity > 0)
            {
                name = name.Substring(0, arity);
            }

            var builder = new StringBuilder(name).Append('<');
            var arguments = type.GenericTypeArguments;
            for (var i = 0; i < arguments.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(PrettyTypeName(arguments[i]));
            }

            return builder.Append('>').ToString();
        }
    }
}
