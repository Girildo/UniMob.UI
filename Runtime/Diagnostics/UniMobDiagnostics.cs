using System;
using JetBrains.Annotations;
using UnityEngine;

namespace UniMob.UI.Diagnostics
{
    /// <summary>
    ///     The one place a layout fault is handed to whoever is listening.
    /// </summary>
    /// <remarks>
    ///     There is no setter. <see cref="Override"/> restores the previous reporter when its scope is
    ///     disposed, which makes "a test left its fake installed" unrepresentable rather than merely
    ///     discouraged. A host that wants a permanent sink calls Override at startup and never disposes.
    /// </remarks>
    public static class UniMobDiagnostics
    {
        private static IDiagnosticsReporter _reporter = new UnityConsoleReporter();

        [NotNull]
        public static IDiagnosticsReporter Reporter => _reporter;

        public static void Report(in LayoutIssue issue)
        {
            _reporter.Report(issue);
        }

        /// <summary>
        ///     Installs <paramref name="reporter"/> until the returned scope is disposed.
        /// </summary>
        public static IDisposable Override([NotNull] IDiagnosticsReporter reporter)
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
        // outlive the session that made it. RenderText's measurer cache resets for the same reason.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            _reporter = new UnityConsoleReporter();
        }

        private sealed class Scope : IDisposable
        {
            private IDiagnosticsReporter _previous;

            public Scope(IDiagnosticsReporter previous) => _previous = previous;

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
