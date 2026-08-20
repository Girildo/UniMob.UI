using System;
using UnityEngine;

namespace UniMob.UI.Diagnostics
{
    /// <summary>
    ///     The one place an exception caught on a caller's behalf is handed to whoever is listening.
    /// </summary>
    /// <remarks>
    ///     There is no setter. <see cref="Override"/> restores the previous reporter when its scope is
    ///     disposed, which makes "a test left its fake installed" unrepresentable rather than merely
    ///     discouraged. A host that wants a permanent sink calls Override at startup and never disposes.
    ///     <para>
    ///         Separate from <see cref="UniMobDiagnostics"/> because the two report different things to
    ///         different audiences: a layout issue is a protocol violation the layout system recovered
    ///         from, and a fault is an exception nothing recovered from.
    ///     </para>
    /// </remarks>
    public static class UniMobError
    {
        private static IErrorReporter _reporter = new UnityConsoleErrorReporter();

        public static IErrorReporter Reporter => _reporter;

        public static void Report(in UniMobFault fault)
        {
            _reporter.Report(fault);
        }

        /// <summary>
        ///     Installs <paramref name="reporter"/> until the returned scope is disposed.
        /// </summary>
        public static IDisposable Override(IErrorReporter reporter)
        {
            if (reporter == null)
            {
                throw new ArgumentNullException(nameof(reporter));
            }

            var scope = new Scope(_reporter);
            _reporter = reporter;
            return scope;
        }

        // A process-global static survives a domain reload under Enter Play Mode Options, so an
        // override installed by an editor tool -- or a fake left behind by a crashed test -- would
        // outlive the session that made it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            _reporter = new UnityConsoleErrorReporter();
        }

        private sealed class Scope : IDisposable
        {
            private IErrorReporter? _previous;

            public Scope(IErrorReporter previous) => _previous = previous;

            public void Dispose()
            {
                if (_previous == null)
                {
                    return;
                }

                _reporter = _previous;
                _previous = null;
            }
        }
    }
}
