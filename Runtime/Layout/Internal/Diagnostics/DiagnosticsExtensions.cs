using System.Collections.Generic;

namespace UniMob.UI.Layout.Internal.Diagnostics;

public static class DiagnosticsExtensions
{
    /// <summary>
    /// Prints the widget hierarchy started from the given state and travelling up the tree.
    /// Deeper level are indented with two spaces per level. The distance traveled upwards can be specified.
    /// </summary>    
    public static string PrintHierarchy(this IState state, int maxLevels = 8)
    {
        var current = state;
        var q = new Queue<string>();
        var sbuilder = new System.Text.StringBuilder();

        // RenderObject.Constraints is an atom, and every caller reports from inside a sizing pass --
        // a tracked computation. Read normally, describing the tree would subscribe the reporting
        // render object's layout to each ancestor's constraints, so every later ancestor layout would
        // drive it again for a fault that has not changed. Reporting must observe, never participate.
        using (Atom.NoWatch)
        {
            while (current != null && maxLevels > 0)
            {
                sbuilder.Clear();
                var prettyName = GetPrettyName(current);
                var indent = new string(' ', maxLevels * 2);
                // Nullable, and printed as such, so "nothing has laid this out" stays distinguishable
                // from Tight(0,0) -- which is a plausible-looking box, not an absence.
                var constraints = current.RenderObject?.Constraints;
                var diagnosticInfo = current.GetDiagnosticInfo();
                sbuilder.Append(indent);
                sbuilder.Append("<color=yellow>");
                sbuilder.Append(prettyName);
                sbuilder.Append("</color>");
                sbuilder.Append(" | ");
                sbuilder.Append("Constraints: ");
                sbuilder.Append(constraints);
                if (diagnosticInfo != null)
                {
                    sbuilder.Append(" | ");
                    sbuilder.Append("Info : ");
                    sbuilder.Append(diagnosticInfo);
                }

                // Null on a state that was never mounted, and at the top of the tree, whose parent
                // context carries no state. A diagnostic that throws is worse than no diagnostic:
                // it replaces the fault being reported with one of its own.
                current = current.Context?.Parent?.State;
                maxLevels--;
                q.Enqueue(sbuilder.ToString());
            }
        }

        return string.Join("\n", q);
    }

    public static string GetPrettyName(IState state)
    {
        var runtimeType = state.GetType();
        var sbuilder = new System.Text.StringBuilder(runtimeType.Name);
        if (runtimeType.IsGenericType)
        {
            sbuilder.Append("<");
            sbuilder.AppendJoin<System.Type>(", ", runtimeType.GenericTypeArguments);
            sbuilder.Append(">");
        }
        return sbuilder.ToString();
    }
}