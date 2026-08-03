namespace UniMob.UI.Diagnostics
{
    /// <summary>
    ///     Where a layout fault goes. One method, because a reporter's whole job is to decide how to say
    ///     something the layout system has already decided is worth saying.
    /// </summary>
    /// <remarks>
    ///     Implementations are called from inside a layout pass and must not lay anything out, read an
    ///     atom outside <c>Atom.NoWatch</c>, or throw.
    /// </remarks>
    public interface IDiagnosticsReporter
    {
        void Report(in LayoutIssue issue);
    }
}
