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
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>Address of the HIVE gateway, e.g. "http://10.0.0.2:5008".</summary>
        public string GatewayUrl { get; set; } = "http://10.0.0.2:5008";

        /// <summary>Machine type from the DFM list, e.g. "disp_hm".</summary>
        public string MachineType { get; set; } = "";

        /// <summary>Software version in the DFM's format, e.g. "APL_1.0.1.1_251216_POR".</summary>
        public string SoftwareVersion { get; set; } = "";

        /// <summary>SHA-1 of the main software, sent as MS_SHA1.</summary>
        public string MainSoftwareHash { get; set; } = "";

        /// <summary>Sub-machine number. 0 means the machine as a whole.</summary>
        public int Sequence { get; set; }

        /// <summary>Folder for the HIVE log: what was sent and what came back.</summary>
        public string LogFolder { get; set; } = @"C:\TEAL_Log_Data\HIVE";

        /// <summary>File that keeps the last state, so the state is still right after a restart.</summary>
        public string StateFile { get; set; } = @"C:\Hive\LatestHiveState.ini";

        /// <summary>CSV file that maps PLC alarm codes to HIVE error codes.</summary>
        public string ErrorCodeFile { get; set; } = "";

        /// <summary>AMS Net ID of the PLC, for reading the machine state.</summary>
        public string PlcAmsNetId { get; set; } = "";

        /// <summary>ADS port of the PLC runtime, normally 851.</summary>
        public int PlcAdsPort { get; set; } = 851;

        /// <summary>
        /// The PLC variable that says what state the machine is in. The HIVE state is
        /// worked out from it - see <see cref="HiveStateMachine.MachineStateChanged"/>.
        /// Leave blank to leave the machine state unread.
        /// </summary>
        public string MachineStateSymbol { get; set; } = "";
    }
}
