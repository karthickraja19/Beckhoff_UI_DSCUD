using System;
using System.ComponentModel;
using Nova.Abstractions.Data;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Who is signed in at this terminal, and at what level.
    /// </summary>
    /// <remarks>
    /// Populated from the access check described on Audio SW DFM v5.4 slides 44 and
    /// 52 - the operator swipes a badge, MES returns a name, an access level and a
    /// function team, and those land here. <see cref="Level"/> is the value every
    /// enable/read-only rule in the GUI is decided from.
    /// </remarks>
    public interface IAppSession : INotifyPropertyChanged
    {
        bool IsLoggedIn { get; }

        /// <summary>
        /// Operator's name as the access database holds it. Named UserName for the
        /// screens that already show it in the title bar.
        /// </summary>
        string UserName { get; }

        /// <summary>
        /// The DFM's numbered level, straight from the access record - not derived
        /// from a role string. <see cref="AccessLevel.None"/> when signed out.
        /// </summary>
        AccessLevel Level { get; }

        /// <summary>Function team name returned by the check, e.g. "Apple AME".</summary>
        string Function { get; }

        /// <summary>SI / CM / Apple, for the slide 45 two-step rule.</summary>
        AccessOrganization Organization { get; }

        /// <summary>
        /// The badge this session was opened with. Recorded in the machine log with
        /// every action, per slide 44 ("All L1/L2/L3 actions are recorded in machine
        /// log").
        /// </summary>
        string CardUid { get; }

        /// <summary>
        /// True once a Level 3 SI login has been countersigned by a Level 3 CM badge
        /// (slide 45). Always true for a login that never needed a second step, so a
        /// caller can simply require it before allowing a Level 3 operation.
        /// </summary>
        bool IsVerified { get; }

        DateTime LoginTime { get; }

        /// <summary>
        /// Legacy role string, kept only for screens that still display one. Derived
        /// from <see cref="Level"/>; new code should read the level.
        /// </summary>
        string Role { get; }

        /// <summary>Opens a session from a granted access check.</summary>
        void Login(AccessCheckResult granted);

        /// <summary>
        /// Marks the second step of a Level 3 SI login complete. The countersigning
        /// badge is recorded; the session stays owned by the operator who signed in.
        /// </summary>
        void CompleteVerification(string verifierName, string verifierCardUid);

        void Logout();
    }
}
