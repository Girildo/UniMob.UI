#if UNITY_EDITOR
using System;

namespace UniMob.UI.Internal.Views
{
    /// <summary>
    ///     Builds the hierarchy name a layout view carries in the Editor.
    /// </summary>
    /// <remarks>
    ///     Callers cache the result against the widget type it was built from: Render runs on every
    ///     layout pass, and this allocates.
    /// </remarks>
    internal static class EditorViewName
    {
        private const string StateSuffix = "State";

        /// <summary>
        ///     <paramref name="widgetType"/>'s name without a trailing "State", followed by
        ///     <paramref name="viewLabel"/>.
        /// </summary>
        public static string For(Type widgetType, string viewLabel)
        {
            var name = widgetType.Name;

            // Ordinal and length-guarded, to agree with DiagnosticNode: a culture-sensitive suffix
            // test can answer differently, and an unguarded strip empties a widget named "State".
            if (
                name.Length > StateSuffix.Length
                && name.EndsWith(StateSuffix, StringComparison.Ordinal)
            )
            {
                name = name.Substring(0, name.Length - StateSuffix.Length);
            }

            return name + viewLabel;
        }
    }
}
#endif
