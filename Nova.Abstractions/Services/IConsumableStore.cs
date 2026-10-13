using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Services
{
    /// <summary>What one gantry of a D-SCUD is fitted with (S3.1 PD-04, S3.3 PD-12, slide 37).</summary>
    public sealed class GantryConsumables
    {
        /// <summary>"Front" or "Rear" (slide 16: front is the gantry nearest the input).</summary>
        public string Gantry { get; set; } = string.Empty;

        /// <summary>The valve model chosen on the Setting page, e.g. "EFD-PICO".</summary>
        public string ValveModel { get; set; } = string.Empty;

        /// <summary>Standard valve SN: XXX-YYY-001_YYMMDD.</summary>
        public string ValveSn { get; set; } = string.Empty;

        public DateTime? ValveChanged { get; set; }

        /// <summary>From MES at the valve change (GET_RUBBERBODY_USED_INFO): Units_Produced.</summary>
        public int ValveUnitsProduced { get; set; }

        /// <summary>Valve_Cycles.</summary>
        public int ValveCycles { get; set; }

        /// <summary>Total_Dispense_Time, in seconds.</summary>
        public int ValveDispenseSeconds { get; set; }

        public string NozzleSn { get; set; } = string.Empty;

        public DateTime? NozzleChanged { get; set; }

        public string GlueSn { get; set; } = string.Empty;

        public DateTime? GlueChanged { get; set; }

        /// <summary>
        /// Set by a glue change: purge_idx goes back to 0 (slide 28). Whatever counts the
        /// purges clears it once it has done so.
        /// </summary>
        public bool PurgeIndexResetPending { get; set; }

        public GantryConsumables Clone()
        {
            return (GantryConsumables)MemberwiseClone();
        }
    }

    /// <summary>One valve model of the Valve model setting (S3.1).</summary>
    public sealed class ValveModel
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// True when the SN is read from the valve controller rather than scanned
        /// ("Internal Serial Number", S3.1.2); MES turns it into the standard SN.
        /// </summary>
        public bool InternalSn { get; set; }
    }

    /// <summary>
    /// The valve, nozzle, glue and material each gantry is fitted with - what the
    /// planned-downtime procedures record and HIVE reports with every unit.
    /// </summary>
    public interface IConsumableStore
    {
        /// <summary>One entry per gantry, front first.</summary>
        IReadOnlyList<GantryConsumables> Gantries { get; }

        /// <summary>The models the Valve model setting offers.</summary>
        IReadOnlyList<ValveModel> ValveModels { get; }

        /// <summary>Material SNs recorded at the last Material Replacement (PD-08), by material.</summary>
        IReadOnlyDictionary<string, string> Materials { get; }

        /// <summary>The materials PD-08 asks for, e.g. "Foam".</summary>
        IReadOnlyList<string> MaterialNames { get; }

        bool SaveGantry(GantryConsumables gantry, out string error);

        bool SaveMaterials(IDictionary<string, string> materials, out string error);

        event EventHandler Changed;
    }
}
