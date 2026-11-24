using System;
using System.Collections.Generic;

namespace Nova.Hive
{
    /// <summary>The limits of one numeric key; either side may be missing.</summary>
    public sealed class HiveLimit
    {
        public double? Lower { get; set; }

        public double? Upper { get; set; }
    }

    /// <summary>
    /// One finished unit, which becomes one machine_data payload (HIVE PayloadSpec 5.6
    /// MACHINEDATA and Audio SW DFM v5.4 S1.2).
    /// </summary>
    /// <remarks>
    /// Where a key goes is the payload's rule, not the caller's choice: text values are
    /// <see cref="Serials"/>, numbers are <see cref="Data"/>, and a number's spec limits are
    /// <see cref="Limits"/> under the same key. A key the machine could not collect is left
    /// out - it is never filled in with a made-up value.
    /// </remarks>
    public sealed class HiveUnitData
    {
        public string UnitSn { get; set; }

        /// <summary>
        /// True for a unit tested in an audit run (LAD, CCD repeatability, pose calibration).
        /// The gateway's answers 1024 and 1025 then mean the upload succeeded (S3.4, S3.5): the
        /// audit keys are not in the limits it checks against.
        /// </summary>
        public bool IsAudit { get; set; }

        public bool Pass { get; set; }

        /// <summary>The unit entered the machine (or its first operation started).</summary>
        public DateTime InputTime { get; set; }

        /// <summary>Every task for the unit at this station was done.</summary>
        public DateTime OutputTime { get; set; }

        /// <summary>Text values: glue_sn, valve_sn, station_id, mac_addr, machine_type, failure_code, ...</summary>
        public IDictionary<string, string> Serials { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Numbers, and the versions the spec keeps here: unit_ct, output_ct, sw_version,
        /// limits_version, ver_dig, sequence, the process parameters, ...
        /// </summary>
        public IDictionary<string, object> Data { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>Spec limits of keys in <see cref="Data"/>, by the same key.</summary>
        public IDictionary<string, HiveLimit> Limits { get; } = new Dictionary<string, HiveLimit>(StringComparer.Ordinal);

        /// <summary>
        /// Files sent to Insight with the unit (glue path AOI images, as one zip), by full
        /// path. The name of each must follow the DFM's blob naming rule (slide 20).
        /// </summary>
        public IList<string> BlobFiles { get; } = new List<string>();
    }

    /// <summary>What became of one machine_data payload.</summary>
    public enum HiveUnitOutcome
    {
        /// <summary>HIVE is switched off: written to the log only.</summary>
        NotSent,

        /// <summary>200/201 from the gateway, and it found nothing wrong with the data.</summary>
        Accepted,

        /// <summary>
        /// 200/201, but the gateway's own check failed the unit: a value out of its limits,
        /// a missing or unknown key ("Result":"FAIL", or an ErrorCode).
        /// </summary>
        Refused,

        /// <summary>No 200/201 in time: HIVE did not confirm it has the unit.</summary>
        NotConfirmed,
    }

    public sealed class HiveUnitResult
    {
        public HiveUnitData Unit { get; set; }

        public HiveUnitOutcome Outcome { get; set; }

        /// <summary>The gateway's answer, or why there was none.</summary>
        public string Detail { get; set; }
    }
}
