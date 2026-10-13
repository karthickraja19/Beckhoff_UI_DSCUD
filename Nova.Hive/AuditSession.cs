using System;
using System.Collections.Generic;

namespace Nova.Hive
{
    /// <summary>
    /// An audit run in planned downtime: LAD (PD-11, slides 68-70), CCD repeatability
    /// testing (PD-13, slide 72) or dispensing pose calibration (PD-14, slide 74).
    /// </summary>
    /// <remarks>
    /// Every unit tested in the run carries the LAD keys in its machine_data:
    /// <c>"audit": true, "Mode": 1|2|3, "Operator_ID": 1|2|3, "TestSeriesID": yyyyMMddHHmmss</c>
    /// (CPK = 1, GRR = 2, SCS = 3; GRR uses Operator_ID 1 to 3). TestSeriesID is taken when
    /// the run starts, so it is new for every planned downtime.
    /// </remarks>
    public sealed class AuditSession
    {
        public const string Cpk = "CPK";
        public const string Grr = "GRR";
        public const string Scs = "SCS";

        public AuditSession(string code, string analysis, string repeatability, DateTime started)
        {
            Code = code;
            Analysis = analysis;
            Repeatability = repeatability;
            TestSeriesId = long.Parse(started.ToString("yyyyMMddHHmmss"));
            OperatorIndex = 1;
        }

        /// <summary>PD-11, PD-13 or PD-14.</summary>
        public string Code { get; }

        /// <summary>CPK, GRR or SCS.</summary>
        public string Analysis { get; }

        /// <summary>PD-13 only: static, dynamic_1 or dynamic_2.</summary>
        public string Repeatability { get; }

        /// <summary>Mode key: CPK 1, GRR 2, SCS 3.</summary>
        public int Mode
        {
            get { return Analysis == Grr ? 2 : Analysis == Scs ? 3 : 1; }
        }

        /// <summary>Operator_ID: 1, 2 or 3 for GRR; always 1 otherwise.</summary>
        public int OperatorIndex { get; private set; }

        public long TestSeriesId { get; }

        /// <summary>Units tested in the current round.</summary>
        public int UnitsTested { get; private set; }

        /// <summary>
        /// Units in one round (slides 69-70): at least 32 for CPK, 10 x 3 for GRR (per
        /// operator), 10 for SCS; 100 for a CCD repeatability run.
        /// </summary>
        public int UnitsPerRound
        {
            get
            {
                if (Code == "PD-13")
                    return 100;
                if (Analysis == Cpk)
                    return 32;
                if (Analysis == Grr)
                    return 30;
                return 10;
            }
        }

        /// <summary>True for GRR while another operator is still to test.</summary>
        public bool HasNextOperator
        {
            get { return Analysis == Grr && OperatorIndex < 3; }
        }

        /// <summary>The keys every audit unit carries in machine_data.</summary>
        public IDictionary<string, object> MachineDataKeys
        {
            get
            {
                return new Dictionary<string, object>
                {
                    { "audit", true },
                    { "Mode", Mode },
                    { "Operator_ID", OperatorIndex },
                    { "TestSeriesID", TestSeriesId },
                };
            }
        }

        public string Describe()
        {
            string what = Code == "PD-11" ? "LAD" : Code == "PD-13" ? "CCD repeatability" : "Dispensing pose calibration";
            return what + " " + Analysis + (Repeatability != null ? " (" + Repeatability + ")" : "")
                + ", Mode " + Mode + ", Operator_ID " + OperatorIndex + ", TestSeriesID " + TestSeriesId
                + ": " + UnitsTested + " of " + UnitsPerRound + " units";
        }

        /// <summary>Counts one unit; true when the round is complete.</summary>
        internal bool CountUnit()
        {
            UnitsTested++;
            return UnitsTested >= UnitsPerRound;
        }

        /// <summary>Another round with the same operator ("Continue test").</summary>
        internal void ContinueSameOperator()
        {
            UnitsTested = 0;
        }

        /// <summary>GRR: the next operator (Operator_ID 2, then 3).</summary>
        internal void NextOperator()
        {
            OperatorIndex++;
            UnitsTested = 0;
        }
    }
}
