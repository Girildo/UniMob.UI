namespace UniMob.UI.Diagnostics
{
    /// <summary>
    ///     Where a caught exception goes. One method, mirroring <see cref="IDiagnosticsReporter"/>: a
    ///     reporter's whole job is to decide how to say something the framework has already decided is
    ///     worth saying.
    /// </summary>
    /// <remarks>
    ///     Implementations are called from inside a <c>catch</c> block that is standing in for a caller
    ///     and must not throw: an exception here replaces the fault being reported with one of its own,
    ///     in a place with nothing left to catch it.
    /// </remarks>
    public interface IErrorReporter
    {
        void Report(in UniMobFault fault);
    }
}
