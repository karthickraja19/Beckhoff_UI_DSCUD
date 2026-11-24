using System;
using System.Collections.Generic;

namespace Nova.Hive
{
    /// <summary>
    /// An audit run in planned downtime: LAD (PD-11, slides 68-70), CCD repeatability
    /// testing (PD-13, slide 72; S3.4 V3.0) or dispensing pose calibration (PD-14, slide 74;
    /// S3.5 V3.0).
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

        public const string CcdRepeatabilityCode = "PD-13";
        public const string PoseCalibrationCode = "PD-14";

        private bool _finalTen;

        public AuditSession(string code, string analysis, string repeatability, DateTime started)
        {
            Code = code;
            Analysis = analysis;
            Repeatability = repeatability;
            TestSeriesId = long.Parse(started.ToString("yyyyMMddHHmmss"));
            OperatorIndex = 1;
            Round = 1;
        }

        /// <summary>PD-11, PD-13 or PD-14.</summary>
        public string Code { get; }

        /// <summary>CPK, GRR or SCS.</summary>
        public string Analysis { get; }

        /// <summary>PD-13 only: static, dynamic_1 or dynamic_2.</summary>
        public string Repeatability { get; }

        /// <summary>
        /// PD-13: which test the captures belong to - 1 static, 2 dynamic (CCD held static),
        /// 3 dynamic (UC held static). The key is <c>{gantry}_delta_{x|y}_test{n}</c>.
        /// </summary>
        public int TestNumber
        {
            get { return Repeatability == "dynamic_1" ? 2 : Repeatability == "dynamic_2" ? 3 : 1; }
        }

        /// <summary>The test SN scanned when the run was started (PD-14: "Scanning SN"); null when none was.</summary>
        public string TestSn { get; set; }

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

        /// <summary>Counts up with every new round (continue, next operator), so a round's own data can start again.</summary>
        public int Round { get; private set; }

        /// <summary>
        /// True once the test was ended with Cancel: the machine stays in the planned
        /// downtime and no further unit is taken into this run. Another test needs Running
        /// and then the PD code again (S3.4, S3.5).
        /// </summary>
        public bool IsFinished { get; private set; }

        /// <summary>
        /// Why the run cannot be uploaded - a machine alarm during a pose calibration means
        /// "the calibration process needs to be restarted" (S3.5). Null while it is good.
        /// </summary>
        public string FailedBecause { get; private set; }

        /// <summary>
        /// Units in one round. LAD (slides 69-70): at least 32 for CPK, 10 x 3 for GRR (per
        /// operator), 10 for SCS. CCD repeatability (S3.4): 100 uploads for CPK and SCS; GRR
        /// 30 per operator, then 10 more as operator 3. Pose calibration (S3.5): one.
        /// </summary>
        public int UnitsPerRound
        {
            get
            {
                if (Code == PoseCalibrationCode)
                    return 1;

                if (Code == CcdRepeatabilityCode)
                    return Analysis != Grr ? 100 : _finalTen ? 10 : 30;

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
            get { return Analysis == Grr && OperatorIndex < 3 && !IsFinished; }
        }

        /// <summary>CCD repeatability GRR: operator 3 has done the 30 and the last 10 are still to do.</summary>
        public bool HasFinalTen
        {
            get { return Code == CcdRepeatabilityCode && Analysis == Grr && OperatorIndex == 3 && !_finalTen && !IsFinished; }
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
            string what = Code == "PD-11" ? "LAD" : Code == CcdRepeatabilityCode ? "CCD repeatability" : "Dispensing pose calibration";
            return what + " " + Analysis + (Repeatability != null ? " (" + Repeatability + ")" : "")
                + ", Mode " + Mode + ", Operator_ID " + OperatorIndex + ", TestSeriesID " + TestSeriesId
                + ": " + UnitsTested + " of " + UnitsPerRound + " units"
                + (IsFinished ? " - ended" : "");
        }

        /// <summary>Counts one unit; true when the round is complete.</summary>
        internal bool CountUnit()
        {
            if (IsFinished)
                return false;

            UnitsTested++;
            return UnitsTested >= UnitsPerRound;
        }

        /// <summary>Another round with the same operator ("Continue test").</summary>
        internal void ContinueSameOperator()
        {
            UnitsTested = 0;
            Round++;
        }

        /// <summary>GRR: the next operator (Operator_ID 2, then 3).</summary>
        internal void NextOperator()
        {
            OperatorIndex++;
            UnitsTested = 0;
            Round++;
        }

        /// <summary>CCD repeatability GRR: the last 10, still as operator 3.</summary>
        internal void StartFinalTen()
        {
            _finalTen = true;
            UnitsTested = 0;
            Round++;
        }

        /// <summary>Cancel: the run takes no more units.</summary>
        internal void Finish()
        {
            IsFinished = true;
        }

        /// <summary>The run cannot be uploaded any more; the first reason is kept.</summary>
        internal void Fail(string why)
        {
            if (FailedBecause == null)
                FailedBecause = why;
        }
    }
}
