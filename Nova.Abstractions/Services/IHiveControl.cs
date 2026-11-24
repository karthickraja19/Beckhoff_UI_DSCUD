using System;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// The HIVE Control tab of the Setting page (S4.1 Extra Log for Hive Remote
    /// Qualification, page 5), and what other screens need to know about the HIVE state.
    /// </summary>
    /// <remarks>
    /// "HIVE IS LINE CRITICAL. HIVE CANNOT BE DISABLED WITHOUT AME APPROVAL IN ADVANCE."
    /// Disabling is only for a network fault or an incomplete HIVE setup, needs Level 3
    /// and a second approval, and is recorded with both names and the reason.
    /// </remarks>
    public interface IHiveControl
    {
        /// <summary>False when this HMI has no HIVE reporting.</summary>
        bool IsAvailable { get; }

        /// <summary>True when payloads are sent to the gateway.</summary>
        bool IsEnabled { get; }

        /// <summary>Who disabled HIVE, when and why; empty while enabled.</summary>
        string DisabledNote { get; }

        /// <summary>Switches sending to the gateway on or off, and records it.</summary>
        /// <returns>False with the reason when refused.</returns>
        bool SetEnabled(bool enabled, string reason, string operatorName, string approverName, out string error);

        /// <summary>
        /// The 24-hour dry run function module for FAT remote qualification (default
        /// Disabled, and reset to Disabled every time the software starts).
        /// </summary>
        bool IsDryRunEnabled { get; }

        void SetDryRun(bool enabled, string operatorName);

        /// <summary>
        /// Why the dry run cannot be enabled on this machine - no SI code, or the remote
        /// qualification DLLs (UUID.dll, UMSID.dll) are not beside the software. Null when it can.
        /// </summary>
        string DryRunUnavailableReason { get; }

        /// <summary>1, 2 or 5; 0 when HIVE is not available.</summary>
        int CurrentState { get; }

        /// <summary>The downtime code while in state 5, e.g. "PD-15"; null otherwise.</summary>
        string CurrentDowntimeCode { get; }

        /// <summary>Raised when any of the above changes. May be raised on any thread.</summary>
        event EventHandler Changed;
    }
}
