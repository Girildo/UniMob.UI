using System;
using System.Collections;
using System.Collections.Generic;
using UniMob.UI.Diagnostics;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Installs itself as the diagnostics reporter for the length of a <c>using</c> block and keeps
    ///     every issue reported inside it.
    /// </summary>
    /// <remarks>
    ///     A test asserting on Unity's log has to spell the message out, which pins the wording rather
    ///     than the fault. Worse, <c>LogAssert.NoUnexpectedReceived</c> goes vacuous the moment reports
    ///     stop reaching Unity's log at all -- it passes just as happily when the widget under test is
    ///     screaming into a recorder nobody checked. Assert on this instead.
    ///     <para>
    ///         Lives in the test assembly rather than in Runtime, so no test helper ships in the package.
    ///     </para>
    /// </remarks>
    public sealed class RecordingReporter
        : IDiagnosticsReporter,
            IDisposable,
            IReadOnlyList<LayoutIssue>
    {
        private readonly List<LayoutIssue> _issues = new List<LayoutIssue>();
        private readonly IDisposable _scope;

        private RecordingReporter()
        {
            _scope = UniMobDiagnostics.Override(this);
        }

        public static RecordingReporter Capture() => new RecordingReporter();

        public int Count => _issues.Count;

        public LayoutIssue this[int index] => _issues[index];

        public void Report(in LayoutIssue issue) => _issues.Add(issue);

        public void Dispose() => _scope.Dispose();

        public IEnumerator<LayoutIssue> GetEnumerator() => _issues.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
