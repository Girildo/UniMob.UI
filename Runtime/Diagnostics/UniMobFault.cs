namespace UniMob.UI.Diagnostics
{
    /// <summary>
    ///     One exception that escaped a lifecycle callback, as data. Carries no formatted text, so each
    ///     reporter formats it its own way. Distinct from a <see cref="LayoutIssue"/> -- see CONTEXT.md.
    /// </summary>
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
        ///     Per-site rather than a closed enum: the sites do not form one vocabulary.
        /// </summary>
        public string Phase { get; }

        /// <summary>The state whose callback threw, where the site knows one.</summary>
        public IState? Subject { get; }
    }
}
