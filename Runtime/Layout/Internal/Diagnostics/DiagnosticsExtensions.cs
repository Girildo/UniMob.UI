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


            current = current.Context.Parent.State;
            maxLevels--;
            q.Enqueue(sbuilder.ToString());
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