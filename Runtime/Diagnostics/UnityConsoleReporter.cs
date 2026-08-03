using UnityEngine;

namespace UniMob.UI.Diagnostics
{
    /// <summary>
    ///     Writes a layout fault to Unity's console, with the offending GameObject attached so the
    ///     entry selects it when clicked.
    /// </summary>
    /// <remarks>
    ///     The context object is the single most useful property one of these messages has, and it is
    ///     also the reason the package owns a reporter interface of its own rather than logging through
    ///     <c>ILogger</c>: a logging abstraction has no channel to carry a <c>GameObject</c> through.
    ///     <para>
    ///         Rich text is decided here rather than in the composed message, so a reporter writing to a
    ///         file is not handed markup it would have to strip.
    ///     </para>
    /// </remarks>
    public sealed class UnityConsoleReporter : IDiagnosticsReporter
    {
        public void Report(in LayoutIssue issue)
        {
            var message = LayoutIssueText.Compose(issue);
            var context = ContextObject(issue);

            if (LayoutIssueText.Severity(issue.Code) == LogType.Warning)
            {
                Debug.LogWarning(message, context);
            }
            else
            {
                Debug.LogError(message, context);
            }
        }

        // The culprit is what a reader wants selected -- the child that did not fit, not the row that
        // noticed. Both are build-only often enough that neither is guaranteed to have a GameObject,
        // and a null context is simply an entry that does not select anything.
        private static Object ContextObject(in LayoutIssue issue)
        {
            using (Atom.NoWatch)
            {
                return ViewObject(issue.Culprit) ?? ViewObject(issue.Subject);
            }
        }

        private static Object ViewObject(IState state)
        {
            try
            {
                // Reaching InnerViewState through a build-only state resolves its child, which by this
                // point is already built -- laying the state out required it. Guarded anyway: a report
                // that throws replaces the fault being reported with one of its own.
                var view = state?.InnerViewState?.MountedView;
                return view is { IsDestroyed: false } ? view.gameObject : null;
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
