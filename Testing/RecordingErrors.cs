using System;
using System.Collections;
using System.Collections.Generic;
using UniMob.UI.Diagnostics;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Installs itself as the error reporter for the length of a <c>using</c> block and keeps every
    ///     fault reported inside it.
    /// </summary>
    /// <remarks>
    ///     The counterpart to <see cref="RecordingReporter"/>, and separate for the same reason the two
    ///     sinks are separate: a layout issue is a protocol violation the layout system recovered from,
    ///     and a fault is an exception nothing recovered from.
    ///     <para>
    ///         What it replaces is <c>LogAssert.ignoreFailingMessages</c>, which suppresses every message
    ///         including the ones a regression produces -- a fixture that expects one fault and silences
    ///         the console cannot tell that it got three. Assert on this instead.
    ///     </para>
    /// </remarks>
    public sealed class RecordingErrors : IErrorReporter, IDisposable, IReadOnlyList<UniMobFault>
    {
        private readonly List<UniMobFault> _faults = new List<UniMobFault>();
        private readonly IDisposable _scope;

        private RecordingErrors()
        {
            _scope = UniMobError.Override(this);
        }

        public static RecordingErrors Capture() => new RecordingErrors();

        public int Count => _faults.Count;

        public UniMobFault this[int index] => _faults[index];

        public void Report(in UniMobFault fault) => _faults.Add(fault);

        public void Dispose() => _scope.Dispose();

        public IEnumerator<UniMobFault> GetEnumerator() => _faults.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
