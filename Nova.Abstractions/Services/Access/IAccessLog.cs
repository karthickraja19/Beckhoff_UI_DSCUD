namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Records access events to the machine log.
    /// </summary>
    /// <remarks>
    /// Audio SW DFM v5.4 slide 44: "All L1/L2/L3 actions are recorded in machine
    /// log". Every entry carries the operator and the badge, because the point of the
    /// requirement is that a change on this machine can be traced to a person.
    ///
    /// Separate from the running-log feed (IRunningLogService), which is a transient
    /// on-screen tail. This one is an audit trail and has to survive a restart.
    /// </remarks>
    public interface IAccessLog
    {
        /// <summary>A granted or refused login attempt.</summary>
        void LoginAttempt(string cardUid, bool granted, string name, int level, string detail);

        /// <summary>The second step of a Level 3 SI login (slide 45).</summary>
        void VerificationCompleted(string operatorName, string verifierName, string verifierCardUid);

        /// <summary>Sign-out, whether operator-initiated or by the idle timeout.</summary>
        void Logout(string name, int level, string reason);

        /// <summary>
        /// An action taken while signed in. Callers pass what changed; the log adds
        /// who and when.
        /// </summary>
        void Action(string name, int level, string action);
    }
}
