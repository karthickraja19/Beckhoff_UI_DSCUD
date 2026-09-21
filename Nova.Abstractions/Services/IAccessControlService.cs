using System.Threading.Tasks;
using Nova.Abstractions.Data;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Which organisation an operator belongs to. Audio SW DFM v5.4 slide 45 gates
    /// two-step verification on this: a Level 3 login by an SI user needs a second
    /// approval, a Level 3 login by a CM user does not.
    /// </summary>
    /// <remarks>
    /// The DFM names SI and CM but does not give a rule for deriving either from the
    /// access database's Function column ("Apple AME", "ICT DFx", ...). It is
    /// therefore stored per record rather than inferred, so the classification comes
    /// from whoever owns the access data instead of from a guess in this code.
    /// </remarks>
    public enum AccessOrganization
    {
        /// <summary>Not stated by the access record.</summary>
        Unknown = 0,

        /// <summary>System integrator - the machine vendor.</summary>
        SI = 1,

        /// <summary>Contract manufacturer - the factory operating the line.</summary>
        CM = 2,

        /// <summary>Apple.</summary>
        Apple = 3
    }

    /// <summary>
    /// One row of the access database - Audio SW DFM v5.4 slide 52 "Sample Access
    /// Database". Column names are the slide's.
    /// </summary>
    public sealed class AccessRecord
    {
        public string Site { get; set; }

        public string Project { get; set; }

        public string StationId { get; set; }

        /// <summary>
        /// Slide 52: <c>Site_Building-Floor-LineSide_Instance_GHDS ID</c>, all
        /// capitalised, e.g. <c>ITKS_E03-4FT-01B_1_STATION59</c>. An access check
        /// matches on this, so a badge valid on one terminal is not valid on another.
        /// </summary>
        public string TerminalName { get; set; }

        /// <summary>Operator's name, returned to the GUI on a successful check.</summary>
        public string Name { get; set; }

        /// <summary>1, 2 or 3 - the DFM's user levels (slide 39).</summary>
        public int AccessLevel { get; set; }

        /// <summary>"Main" or "Vision" (slide 52). Distinguishes the two applications.</summary>
        public string SoftwareType { get; set; }

        /// <summary>The badge's UID, as the reader reports it.</summary>
        public string CardUid { get; set; }

        /// <summary>Function team name, e.g. "Apple AME" - returned by the check.</summary>
        public string Function { get; set; }

        /// <summary>SI / CM / Apple, for the slide 45 two-step rule.</summary>
        public AccessOrganization Organization { get; set; }
    }

    /// <summary>
    /// What the access check returned - the "Return name, access level and function
    /// team name" / "Return False" branches of slide 52's flow.
    /// </summary>
    public sealed class AccessCheckResult
    {
        public bool Granted { get; private set; }

        public string Name { get; private set; }

        public AccessLevel Level { get; private set; }

        public string Function { get; private set; }

        public AccessOrganization Organization { get; private set; }

        public string CardUid { get; private set; }

        /// <summary>Why the check failed; empty when it succeeded.</summary>
        public string FailureReason { get; private set; }

        public static AccessCheckResult Deny(string reason)
        {
            return new AccessCheckResult { Granted = false, FailureReason = reason ?? "Access denied." };
        }

        public static AccessCheckResult Allow(
            string name, AccessLevel level, string function, AccessOrganization organization, string cardUid)
        {
            return new AccessCheckResult
            {
                Granted = true,
                Name = name,
                Level = level,
                Function = function,
                Organization = organization,
                CardUid = cardUid,
                FailureReason = string.Empty
            };
        }
    }

    /// <summary>
    /// The access check behind the login pop-up - Audio SW DFM v5.4 slides 44 and 52.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Slide 52 defines the flow as a round trip to MES: the operator swipes a card,
    /// the machine sends <c>terminalName</c>, <c>SoftwareType</c> and <c>Card UID</c>,
    /// MES resolves the user ID and checks it against the access database, and returns
    /// either the name, access level and function team, or false.
    /// </para>
    /// <para>
    /// This interface is that call and nothing more, which is why it is asynchronous
    /// and returns a result object rather than throwing: a machine with no MES link
    /// must still be able to tell the operator why they were refused. The shipped
    /// implementation reads a local copy of the access database; replacing it with a
    /// real MES client is a DI registration change and touches no caller.
    /// </para>
    /// </remarks>
    public interface IAccessControlService
    {
        /// <summary>
        /// This machine's terminal name, in slide 52's format. Sent with every check
        /// and shown on the login screen so the operator can see which terminal they
        /// are being granted access to.
        /// </summary>
        string TerminalName { get; }

        /// <summary>"Main" or "Vision" - which application this is (slide 52).</summary>
        string SoftwareType { get; }

        /// <summary>
        /// Resolves one swiped card against the access database for this terminal.
        /// Never throws for an ordinary denial - an unreadable database or an
        /// unreachable MES comes back as a denial carrying the reason.
        /// </summary>
        Task<AccessCheckResult> CheckAccessAsync(string cardUid);
    }
}
