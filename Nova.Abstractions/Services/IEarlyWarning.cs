using System;
using System.Threading.Tasks;

namespace Nova.Abstractions.Services
{
    /// <summary>One request to MES for the early-warning test box, as sent and as answered.</summary>
    public sealed class EarlyWarningTest
    {
        /// <summary>The request body, as shown in "Request body".</summary>
        public string Request { get; set; }

        /// <summary>MES's answer, as shown in "Response body"; or why there was none.</summary>
        public string Response { get; set; }

        /// <summary>True when MES answered Result false: a warning.</summary>
        public bool IsWarning { get; set; }
    }

    /// <summary>
    /// S17 Early Warning For Identifying Common Defect Factors (X3): after each unit passes
    /// the station MES is asked <c>GET_SMART_FA_ALTER_MSG</c>; an answer of false opens a
    /// warning pop-up with MES's text. The machine does not stop.
    /// </summary>
    public interface IEarlyWarning
    {
        /// <summary>False when no MES address is configured: nothing is asked.</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// False while the pop-up and buzzer are disabled. The request to MES is still sent.
        /// Enabled again every time the software starts.
        /// </summary>
        bool IsPopupEnabled { get; }

        /// <summary>True when closing the pop-up and changing the switch need a card checked with MES.</summary>
        bool NeedsCard { get; }

        /// <summary>The terminal name the machine sends, offered in the test box.</summary>
        string TerminalName { get; }

        /// <summary>
        /// Enables or disables the pop-up and buzzer. Where a card is needed this asks for
        /// the CM DRI's card and checks it with MES ("UID,Authorize") first.
        /// </summary>
        /// <returns>Null when changed; otherwise why not (also when the card window was cancelled).</returns>
        string SetPopupEnabled(bool enabled);

        /// <summary>The test box: one request with this terminal name and serial number.</summary>
        Task<EarlyWarningTest> TestAsync(string terminalName, string serialNumber);

        /// <summary>Raised when <see cref="IsPopupEnabled"/> changes. May be raised on any thread.</summary>
        event EventHandler Changed;
    }
}
