using System.Threading.Tasks;

namespace Nova.Abstractions.Services
{
    /// <summary>What MES answered to one request: <c>{"Result":true,"RetMsg":"..."}</c>.</summary>
    public sealed class MesResult
    {
        /// <summary>MES's own Result: true when it accepted the request.</summary>
        public bool Ok { get; set; }

        /// <summary>RetMsg, or why no answer came back.</summary>
        public string Message { get; set; }

        /// <summary>False when MES was not configured, not reachable, or answered something unreadable.</summary>
        public bool Reached { get; set; }

        public static MesResult NotReached(string why)
        {
            return new MesResult { Ok = false, Reached = false, Message = why };
        }
    }

    /// <summary>
    /// The MES WebAPI used by the machine GUI: <c>/api/Assy/GetCmdResult</c>, one
    /// endpoint whose command is named by <c>pCmd</c> (Access Control V5.1, S3.1 PD-04,
    /// S3.6 PD-15 and the FATP API list).
    /// </summary>
    /// <remarks>
    /// Never throws: an MES that is not configured or not reachable comes back as a
    /// <see cref="MesResult"/> with <see cref="MesResult.Reached"/> false and the reason.
    /// Every request and answer is written to the machine log (DFM slide 96).
    /// </remarks>
    public interface IMesClient
    {
        /// <summary>False when no MES address is configured (MesUrl in App.config).</summary>
        bool IsConfigured { get; }

        /// <summary>The terminalName this machine sends (DFM slide 52).</summary>
        string TerminalName { get; }

        /// <summary>Posts one GetCmdResult request.</summary>
        /// <param name="command">pCmd, e.g. "G_MACHINE_ACCESS_BY_USERNO".</param>
        /// <param name="serialNumber">serial_Number - MES does not accept it empty.</param>
        /// <param name="testData">testData, e.g. "site=ITKS$project=H38$approver_id=1234".</param>
        /// <param name="results">results, e.g. "key=MS_SHA1$value=...".</param>
        /// <param name="empNo">empNo, e.g. "AUTOH59" for the glue warnings; usually empty.</param>
        /// <param name="terminalName">terminalName for this one request; null or blank sends <see cref="TerminalName"/>.</param>
        Task<MesResult> GetCmdResultAsync(string command, string serialNumber, string testData = "", string results = "", string empNo = "", string terminalName = null);

        /// <summary>
        /// Posts one machine-state report to <c>/api/machine/collmachinestatefromcard</c>
        /// (S1.1.2 Machine State Reporting to MES V5.0). The body is built by the caller.
        /// </summary>
        Task<MesResult> PostMachineStateAsync(string json);
    }
}
