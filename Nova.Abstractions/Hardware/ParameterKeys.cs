namespace Nova.Abstractions.Hardware
{
    /// <summary>
    /// Centralized repository of all parameter keys used by the IParameterService.
    /// This prevents "magic strings" and typos when reading/writing configuration values.
    /// </summary>
    public static class ParameterKeys
    {
        // --- Common Motion Keys ---
        public const string Velocity = "Velocity";
        public const string TargetPosition = "TargetPos";

        // --- Advantech Specific Keys ---
        public const string Advantech_Acc = "advan_acc";
        public const string Advantech_Dcc = "advan_dcc";
        public const string Advantech_Jerk = "advan_jerk";

        // --- Inovance / EtherCAT Specific Keys ---
        public const string Inovance_ControlWord = "inovance_cw";
        public const string Inovance_NodeId = "inovance_node_id";
        
        // --- Cylinder Specific Keys ---
        public const string Cylinder_AdvanceDelay = "cyl_adv_delay";
        public const string Cylinder_RetractDelay = "cyl_ret_delay";
    }
}
