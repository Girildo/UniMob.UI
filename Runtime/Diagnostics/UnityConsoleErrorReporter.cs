using UnityEngine;

namespace UniMob.UI.Diagnostics
{
    /// <summary>
    ///     Writes a caught exception to Unity's console, with the offending GameObject attached so the
    ///     entry selects it when clicked.
    /// </summary>
    /// <remarks>
    ///     The entry keeps <see cref="LogType.Exception"/> and the thrown type's own message. The phase
    ///     is not formatted here; it reaches any reporter installed through
    ///     <see cref="UniMobError.Override"/>.
    /// </remarks>
    public sealed class UnityConsoleErrorReporter : IErrorReporter
    {
        public void Report(in UniMobFault fault)
        {
            // LogException, not a composed LogError: naming the phase would mean wrapping the
            // exception, which replaces both the type shown and the stack trace it carries.
            Debug.LogException(fault.Exception, ContextObject(fault.Subject));
        }

        private static Object? ContextObject(IState? subject)
        {
            using (Atom.NoWatch)
            {
                try
                {
                    // A report that throws replaces the fault being reported with one of its own, and
                    // this one is already running where nothing is left to catch it.
                    var view = subject?.InnerViewState?.MountedView;
                    return view is { IsDestroyed: false } ? view.gameObject : null;
                }
                catch (System.Exception)
                {
                    return null;
                }
            }
        }
    }
}
