namespace UniMob.UI.Diagnostics
{
    /// <summary>
    ///     One exception that escaped a lifecycle callback, as data. Carries no formatted text, for the
    ///     same reason <see cref="LayoutIssue"/> does not: a reporter that writes to a console formats
    ///     one way and one that writes to a structured log formats another.
    /// </summary>
    /// <remarks>
    ///     Distinct from a <see cref="LayoutIssue"/>, which is a layout-protocol violation the layout
    ///     system detected and recovered from. A fault is an exception the framework caught on a
    ///     caller's behalf because there was no caller left to throw it to.
    /// </remarks>
    public readonly struct UniMobFault
    {
        public UniMobFault(System.Exception exception, string phase, IState? subject = null)
        {
            this.Exception = exception;
            this.Phase = phase;
            this.Subject = subject;
        }

        /// <summary>What was thrown.</summary>
        public System.Exception Exception { get; }

        /// <summary>
        ///     What the framework was doing when it caught this, written next to the catch that knows.
        /// </summary>
        /// <remarks>
        ///     Per-site rather than a closed enum, for the same reason <see cref="LayoutIssue.Remedy"/>
        ///     is: eight sites are <c>View</c> lifecycle phases and the ninth is a back press, so an
        ///     enum would have to be wrong about one of them.
        /// </remarks>
        public string Phase { get; }

        /// <summary>The state whose callback threw, where the site knows one.</summary>
        public IState? Subject { get; }
    }
}
