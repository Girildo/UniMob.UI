using UnityEngine;

namespace UniMob.UI.Diagnostics
{
    /// <summary>
    ///     Writes a caught exception to Unity's console, with the offending GameObject attached so the
    ///     entry selects it when clicked.
    /// </summary>
    /// <remarks>
    ///     <see cref="Debug.LogException(System.Exception, Object)"/> and not a composed
    ///     <see cref="Debug.LogError(object)"/>: the entry keeps <see cref="LogType.Exception"/> and the
    ///     thrown type's own message, which is what a console reader recognises and what
    ///     <c>LogAssert.Expect</c> matches on. Naming <see cref="UniMobFault.Phase"/> here would mean
    ///     wrapping the exception, which replaces both.
    ///     <para>
    ///         The phase is not lost, only unformatted: it reaches any reporter installed through
    ///         <see cref="UniMobError.Override"/>, which is where a structured sink reads it.
    ///     </para>
    /// </remarks>
    public sealed class UnityConsoleErrorReporter : IErrorReporter
    {
        public void Report(in UniMobFault fault)
        {
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
