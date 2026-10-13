using System;
using System.Collections.Generic;
using System.Linq;

namespace Nova.Abstractions.Data.Models
{
    /// <summary>
    /// One automatic glue weighing: one gantry, one glue path (S2.4.4.5 Glue Weight,
    /// raw data table). Weights are in milligrams, as HIVE's last_glue_weight.
    /// </summary>
    public sealed class GlueWeighingRecord
    {
        public DateTime Time { get; set; }

        /// <summary>"DS" or "NS".</summary>
        public string Shift { get; set; }

        /// <summary>"Front" or "Rear".</summary>
        public string Gantry { get; set; }

        /// <summary>Glue path number, from 1.</summary>
        public int Path { get; set; }

        /// <summary>Dispensing speed of the weighing, mm/s (the raw data table's Speed column).</summary>
        public double DispenseSpeed { get; set; }

        public double WeightMg { get; set; }

        public double NominalMg { get; set; }

        public double LowerLimitMg { get; set; }

        public double UpperLimitMg { get; set; }

        public bool IsPass => WeightMg >= LowerLimitMg && WeightMg <= UpperLimitMg;
    }

    /// <summary>One calibration point behind a glue regression.</summary>
    public sealed class GlueCalibrationPoint
    {
        public GlueCalibrationPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }
    }

    /// <summary>
    /// The linear regression from one gantry and glue path's last calibration - HIVE
    /// keys w, b and R2 (Audio SW DFM v5.4 slide 18). S2.4.4.5 fits it by least squares
    /// to ten points: glue weight (mg) = a x dispensing speed (mm/s) + b, where a is W.
    /// </summary>
    public sealed class GlueRegression
    {
        public string Gantry { get; set; }

        /// <summary>Glue path number, from 1.</summary>
        public int Path { get; set; }

        public double W { get; set; }

        public double B { get; set; }

        public double R2 { get; set; }

        public DateTime? CalibratedAt { get; set; }

        /// <summary>What the regression's input is, e.g. "Dispensing speed (mm/s)".</summary>
        public string InputName { get; set; }

        public IReadOnlyList<GlueCalibrationPoint> Points { get; set; } = new GlueCalibrationPoint[0];
    }

    /// <summary>
    /// When the automatic weighing scale runs (SOP-35401735 "Automatic Weighing Scale":
    /// time, count of cycles, start or end of shift) and how (S2.4.4.5: weighing
    /// times, calibration frequency).
    /// </summary>
    public sealed class GlueWeighingSettings
    {
        public bool ByTime { get; set; }

        public int IntervalMinutes { get; set; }

        public bool ByUnitCount { get; set; }

        public int IntervalUnits { get; set; }

        public bool AtShiftStart { get; set; }

        public bool AtShiftEnd { get; set; }

        /// <summary>Shots dispensed onto the scale for one weighing.</summary>
        public int ShotsPerWeighing { get; set; }

        /// <summary>Hours between glue calibrations (the regression's refresh) - S2.4.4.5's "Calibration frequency".</summary>
        public int CalibrationIntervalHours { get; set; }

        public GlueWeighingSettings Copy()
        {
            return (GlueWeighingSettings)MemberwiseClone();
        }
    }

    /// <summary>
    /// The glue in one gantry's cartridge, as the machine counted it since the last
    /// glue change (S2.4.4.4 section 2.1). Every glue change resets the counts.
    /// </summary>
    public sealed class GlueCartridgeStatus
    {
        public string Gantry { get; set; }

        public string GlueSn { get; set; }

        public DateTime? ChangedAt { get; set; }

        public int ProducedQty { get; set; }

        public int LongPurgeCycles { get; set; }

        public int ShortPurgeCycles { get; set; }

        public int WeighingCycles { get; set; }

        /// <summary>Remaining pot life from MES, in hours. Null when MES has not answered.</summary>
        public double? RemainingPotLifeHours { get; set; }
    }

    /// <summary>One gantry's glue dosages, set by OSS (S2.4.4.4 section 2.1). Grams.</summary>
    public sealed class GantryGlueDosage
    {
        public string Gantry { get; set; }

        public double CartridgeWeightG { get; set; }

        public double GluePerProductG { get; set; }

        public double LongPurgeDoseG { get; set; }

        public double ShortPurgeDoseG { get; set; }

        public GantryGlueDosage Copy()
        {
            return (GantryGlueDosage)MemberwiseClone();
        }
    }

    /// <summary>
    /// The dosages and warning thresholds behind the Health tab (S2.4.4.4). OSS set
    /// them to suit the station.
    /// </summary>
    public sealed class GlueControlSettings
    {
        public List<GantryGlueDosage> Gantries { get; set; } = new List<GantryGlueDosage>();

        /// <summary>Remaining glue at or below this share is "low" and staging is requested. Default 20 %.</summary>
        public double LowGluePercent { get; set; } = 20;

        /// <summary>Remaining glue at or below this share must be changed now. Default 5 %.</summary>
        public double EmptyGluePercent { get; set; } = 5;

        /// <summary>Remaining pot life at or below this many hours is a warning. Default 0.5 h.</summary>
        public double PotLifeWarningHours { get; set; } = 0.5;

        /// <summary>Nozzle health below this is a warning, prompted every 4 hours (PD-12). Default 24.</summary>
        public double NozzleWarningHours { get; set; } = 24;

        /// <summary>
        /// Nozzle health at or below this is critical, prompted every 2 hours (PD-12).
        /// Default 12. At, not below: S2.4.4.4 draws its 12 h rear nozzle red.
        /// </summary>
        public double NozzleCriticalHours { get; set; } = 12;

        public GlueControlSettings Copy()
        {
            var copy = (GlueControlSettings)MemberwiseClone();
            copy.Gantries = Gantries.Select(g => g.Copy()).ToList();
            return copy;
        }
    }

    /// <summary>
    /// One nozzle's health as MES reports it (S2.4.4.4 section 1). The valve SN and
    /// nozzle ID must match what is fitted on the machine.
    /// </summary>
    public sealed class NozzleHealthRecord
    {
        public string Gantry { get; set; }

        public string ValveSn { get; set; }

        public string NozzleId { get; set; }

        /// <summary>Remaining health, in hours.</summary>
        public double Value { get; set; }

        public double LowerLimit { get; set; }

        public double UpperLimit { get; set; }
    }
}
