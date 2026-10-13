using System;
using System.Collections.Generic;
using Nova.Abstractions.Services;

namespace Nova.Hive
{
    /// <summary>
    /// Everything HIVE reporting needs to know about this machine. Filled in at startup
    /// from App.config and the station profile.
    /// </summary>
    public sealed class HiveSettings
    {
        /// <summary>
        /// False on a desk with no HIVE gateway: nothing is sent, but alarms and the
        /// pop-up still work. On the line HIVE may not be switched off without AME approval.
        /// Starts from App.config and can be switched on the HIVE Control tab
        /// (<see cref="HiveControl"/>), so it is read every time something is sent.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>Address of the HIVE gateway, e.g. "http://10.0.0.2:5008".</summary>
        public string GatewayUrl { get; set; } = "http://10.0.0.2:5008";

        /// <summary>Machine type from the DFM list, e.g. "disp_hm".</summary>
        public string MachineType { get; set; } = "";

        /// <summary>Software version in the DFM's format, e.g. "APL_1.0.1.1_251216_POR".</summary>
        public string SoftwareVersion { get; set; } = "";

        /// <summary>
        /// SHA-1 of the main software, sent as MS_SHA1 when <see cref="ReadHashKeys"/> is
        /// not set.
        /// </summary>
        public string MainSoftwareHash { get; set; } = "";

        /// <summary>
        /// Reads every hash key set on the PD-15 page, live (slide 12: "Hash keys are sent
        /// with every machine_state payload ... live values from software"). Called on every
        /// state change. Null sends only <see cref="MainSoftwareHash"/>.
        /// </summary>
        public Func<IReadOnlyList<HashKeyReading>> ReadHashKeys { get; set; }

        /// <summary>
        /// True while the Setting page's Remote Qualification is ON: every machine_state
        /// payload is also written to the remote qualification log (S2.4).
        /// </summary>
        public Func<bool> RemoteQualification { get; set; }

        /// <summary>Sub-machine number. 0 means the machine as a whole.</summary>
        public int Sequence { get; set; }

        /// <summary>Folder for the HIVE log: what was sent and what came back.</summary>
        public string LogFolder { get; set; } = @"C:\TEAL_Log_Data\HIVE";

        /// <summary>File that keeps the last state, so the state is still right after a restart.</summary>
        public string StateFile { get; set; } = @"C:\Hive\LatestHiveState.ini";

        /// <summary>
        /// File that keeps the payloads the gateway has not accepted yet, so they are still
        /// sent after the HMI was closed (HIVE PayloadSpec 5.6). Blank puts it beside
        /// <see cref="StateFile"/> as HivePending.json.
        /// </summary>
        public string PendingFile { get; set; } = "";

        /// <summary>CSV file that maps PLC alarm codes to HIVE error codes.</summary>
        public string ErrorCodeFile { get; set; } = "";

        /// <summary>
        /// The PLC variable that says what state the machine is in. The HIVE state is
        /// worked out from it - see <see cref="HiveStateMachine.MachineStateChanged"/>.
        /// Leave blank to leave the machine state unread.
        /// </summary>
        public string MachineStateSymbol { get; set; } = "";

        /// <summary>
        /// The PLC variable that says a unit entered the machine (slides 7 and 8: Running
        /// follows units, not the PLC mode). Leave blank to keep the 90 s rule that starts
        /// when the PLC leaves auto. See <see cref="PlcUnitReader"/>.
        /// </summary>
        public string UnitEnteredSymbol { get; set; } = "";

        /// <summary>True when <see cref="UnitEnteredSymbol"/> is an INT counter, false for a BOOL pulse.</summary>
        public bool UnitEnteredIsCounter { get; set; }
    }
}
