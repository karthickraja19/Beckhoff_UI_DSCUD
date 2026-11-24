using System;
using System.Collections.Generic;
using System.Linq;

namespace Nova.Wpf.App.Services.Twincat.Mapping
{
    /// <summary>
    /// The finished unit's data, read once each time Status.UnitDoneCount steps up. The PLC
    /// must hold these values until the next unit is counted. <see cref="Pass"/> is required:
    /// a unit without a result is not recorded. Any other member left blank is saved as
    /// unknown.
    /// </summary>
    public sealed class UnitRecordSymbols
    {
        /// <summary>BOOL: TRUE = OK. Required: without it no production row is written.</summary>
        public ParameterSymbol Pass { get; set; } = new ParameterSymbol();

        /// <summary>REAL/LREAL seconds from input to output.</summary>
        public ParameterSymbol CycleTime { get; set; } = new ParameterSymbol();

        /// <summary>BOOL: TRUE when the pre-process CCD tossed the unit.</summary>
        public ParameterSymbol Tossing { get; set; } = new ParameterSymbol();

        /// <summary>INT: 1 for the first pass, higher for a retest.</summary>
        public ParameterSymbol Iteration { get; set; } = new ParameterSymbol();

        /// <summary>INT: the station (1 / 2) that dispensed it.</summary>
        public ParameterSymbol Station { get; set; } = new ParameterSymbol();

        /// <summary>STRING: the HIVE failure code, "NA" for a pass.</summary>
        public ParameterSymbol FailureCode { get; set; } = new ParameterSymbol();

        /// <summary>STRING: the SN of the UC / fixture the unit ran on (HIVE fixture_id).</summary>
        public ParameterSymbol FixtureId { get; set; } = new ParameterSymbol();

        /// <summary>
        /// S16 Glue Path AOI Inspection State: the vision software's state code of each
        /// dispensing pose, by the key HIVE wants ("pose1_aoi_inspection_state", ...). Each is a
        /// DINT / UDINT of seven digits, one per check (no glue, shift, missing, broken,
        /// keep-out, hole, less glue): 1 enabled, 0 disabled, 2 not applicable. A pose listed
        /// here whose value is 0 or cannot be read is sent as 0 with both limits 1111111
        /// (S16 rule 1); a pose left blank is not sent at all.
        /// </summary>
        public Dictionary<string, ParameterSymbol> AoiStates { get; set; }
            = new Dictionary<string, ParameterSymbol>(StringComparer.Ordinal);

        public bool IsMapped => PlcSymbolMap.Has(Pass?.Symbol);

        public IEnumerable<KeyValuePair<string, ParameterSymbol>> All()
        {
            foreach (KeyValuePair<string, ParameterSymbol> pose in AoiStates ?? new Dictionary<string, ParameterSymbol>())
                yield return new KeyValuePair<string, ParameterSymbol>("AoiStates." + pose.Key, pose.Value);

            yield return new KeyValuePair<string, ParameterSymbol>(nameof(Pass), Pass);
            yield return new KeyValuePair<string, ParameterSymbol>(nameof(CycleTime), CycleTime);
            yield return new KeyValuePair<string, ParameterSymbol>(nameof(Tossing), Tossing);
            yield return new KeyValuePair<string, ParameterSymbol>(nameof(Iteration), Iteration);
            yield return new KeyValuePair<string, ParameterSymbol>(nameof(Station), Station);
            yield return new KeyValuePair<string, ParameterSymbol>(nameof(FailureCode), FailureCode);
            yield return new KeyValuePair<string, ParameterSymbol>(nameof(FixtureId), FixtureId);
        }
    }

    /// <summary>One measured coordinate of the finished unit, read with the unit record.</summary>
    public sealed class CoordinatePointSymbol
    {
        /// <summary>The point's name on the Axis tab, e.g. "1" or "Dispense start".</summary>
        public string Point { get; set; } = string.Empty;

        /// <summary>"X", "Y", "Z", "A", "R".</summary>
        public string Axis { get; set; } = string.Empty;

        /// <summary>REAL/LREAL: the measured value.</summary>
        public string Symbol { get; set; } = string.Empty;

        public double? Nominal { get; set; }

        public double? Lsl { get; set; }

        public double? Usl { get; set; }
    }

    public sealed class CoordinateSymbols
    {
        public List<CoordinatePointSymbol> Points { get; set; } = new List<CoordinatePointSymbol>();

        public bool IsMapped => Points != null && Points.Any(p => p != null && PlcSymbolMap.Has(p.Symbol));
    }

    /// <summary>The live glue figures of one dispensing gantry.</summary>
    public sealed class GlueGantrySymbols
    {
        /// <summary>The station (1 / 2) this gantry is: which station's weighings are this gantry's.</summary>
        public int Station { get; set; }

        /// <summary>DINT: units produced on this cartridge.</summary>
        public string ProducedQty { get; set; } = string.Empty;

        public string LongPurgeCycles { get; set; } = string.Empty;

        public string ShortPurgeCycles { get; set; } = string.Empty;

        public string WeighingCycles { get; set; } = string.Empty;

        /// <summary>REAL hours of pot life left.</summary>
        public string RemainingPotLifeHours { get; set; } = string.Empty;

        /// <summary>REAL hours of nozzle life left.</summary>
        public string NozzleHoursLeft { get; set; } = string.Empty;

        /// <summary>The nozzle's full life, hours: the top of the Health tab's bar.</summary>
        public double NozzleLifeHours { get; set; } = 168;

        public IEnumerable<KeyValuePair<string, string>> All()
        {
            yield return new KeyValuePair<string, string>(nameof(ProducedQty), ProducedQty);
            yield return new KeyValuePair<string, string>(nameof(LongPurgeCycles), LongPurgeCycles);
            yield return new KeyValuePair<string, string>(nameof(ShortPurgeCycles), ShortPurgeCycles);
            yield return new KeyValuePair<string, string>(nameof(WeighingCycles), WeighingCycles);
            yield return new KeyValuePair<string, string>(nameof(RemainingPotLifeHours), RemainingPotLifeHours);
            yield return new KeyValuePair<string, string>(nameof(NozzleHoursLeft), NozzleHoursLeft);
        }
    }

    /// <summary>One glue weighing, read each time <see cref="Count"/> steps up.</summary>
    public sealed class GlueWeighingSymbols
    {
        /// <summary>UDINT/DINT counted up by the PLC for every finished weighing.</summary>
        public string Count { get; set; } = string.Empty;

        public ParameterSymbol Station { get; set; } = new ParameterSymbol();       // INT 1 / 2
        public ParameterSymbol Path { get; set; } = new ParameterSymbol();          // INT glue path
        public ParameterSymbol Speed { get; set; } = new ParameterSymbol();         // REAL mm/s
        public ParameterSymbol WeightMg { get; set; } = new ParameterSymbol();      // REAL
        public ParameterSymbol NominalMg { get; set; } = new ParameterSymbol();     // REAL
        public ParameterSymbol LowerLimitMg { get; set; } = new ParameterSymbol();  // REAL
        public ParameterSymbol UpperLimitMg { get; set; } = new ParameterSymbol();  // REAL

        /// <summary>
        /// BOOL: TRUE when the last weighing was started by hand, FALSE for an automatic one.
        /// PD-17 may follow PD-10 only after a manual weighing (DFM slide 31).
        /// </summary>
        public ParameterSymbol IsManual { get; set; } = new ParameterSymbol();

        /// <summary>A weighing is judged against its limits, so the weight, nominal and both limits are required.</summary>
        public bool IsMapped => PlcSymbolMap.Has(Count) && PlcSymbolMap.Has(WeightMg?.Symbol) && PlcSymbolMap.Has(NominalMg?.Symbol)
            && PlcSymbolMap.Has(LowerLimitMg?.Symbol) && PlcSymbolMap.Has(UpperLimitMg?.Symbol);

        public IEnumerable<KeyValuePair<string, ParameterSymbol>> Members()
        {
            yield return new KeyValuePair<string, ParameterSymbol>(nameof(Station), Station);
            yield return new KeyValuePair<string, ParameterSymbol>(nameof(Path), Path);
            yield return new KeyValuePair<string, ParameterSymbol>(nameof(Speed), Speed);
            yield return new KeyValuePair<string, ParameterSymbol>(nameof(WeightMg), WeightMg);
            yield return new KeyValuePair<string, ParameterSymbol>(nameof(NominalMg), NominalMg);
            yield return new KeyValuePair<string, ParameterSymbol>(nameof(LowerLimitMg), LowerLimitMg);
            yield return new KeyValuePair<string, ParameterSymbol>(nameof(UpperLimitMg), UpperLimitMg);
        }
    }

    /// <summary>The weight-against-speed regression of one gantry's glue path, from its last calibration.</summary>
    public sealed class GlueRegressionSymbols
    {
        public string Gantry { get; set; } = string.Empty;

        public int Path { get; set; }

        public string W { get; set; } = string.Empty;   // LREAL slope, mg per mm/s
        public string B { get; set; } = string.Empty;   // LREAL intercept, mg
        public string R2 { get; set; } = string.Empty;  // LREAL

        public bool IsMapped => PlcSymbolMap.Has(W) && PlcSymbolMap.Has(B);
    }

    /// <summary>The PLC variables behind the Glue tab's weighing settings. Written on Save and each time the PLC comes back.</summary>
    public sealed class GlueWeighingSettingSymbols
    {
        public string ByTime { get; set; } = string.Empty;                    // BOOL
        public string IntervalMinutes { get; set; } = string.Empty;           // INT
        public string ByUnitCount { get; set; } = string.Empty;               // BOOL
        public string IntervalUnits { get; set; } = string.Empty;             // INT
        public string AtShiftStart { get; set; } = string.Empty;              // BOOL
        public string AtShiftEnd { get; set; } = string.Empty;                // BOOL
        public string ShotsPerWeighing { get; set; } = string.Empty;          // INT
        public string CalibrationIntervalHours { get; set; } = string.Empty;  // INT

        public IEnumerable<KeyValuePair<string, string>> All()
        {
            yield return new KeyValuePair<string, string>(nameof(ByTime), ByTime);
            yield return new KeyValuePair<string, string>(nameof(IntervalMinutes), IntervalMinutes);
            yield return new KeyValuePair<string, string>(nameof(ByUnitCount), ByUnitCount);
            yield return new KeyValuePair<string, string>(nameof(IntervalUnits), IntervalUnits);
            yield return new KeyValuePair<string, string>(nameof(AtShiftStart), AtShiftStart);
            yield return new KeyValuePair<string, string>(nameof(AtShiftEnd), AtShiftEnd);
            yield return new KeyValuePair<string, string>(nameof(ShotsPerWeighing), ShotsPerWeighing);
            yield return new KeyValuePair<string, string>(nameof(CalibrationIntervalHours), CalibrationIntervalHours);
        }

        public bool IsMapped => All().Any(m => PlcSymbolMap.Has(m.Value));
    }

    public sealed class GlueSymbols
    {
        /// <summary>By gantry name as the consumables use it ("Front", "Rear").</summary>
        public Dictionary<string, GlueGantrySymbols> Gantries { get; set; }
            = new Dictionary<string, GlueGantrySymbols>(StringComparer.OrdinalIgnoreCase);

        public GlueWeighingSymbols Weighing { get; set; } = new GlueWeighingSymbols();

        public List<GlueRegressionSymbols> Regressions { get; set; } = new List<GlueRegressionSymbols>();

        public GlueWeighingSettingSymbols WeighingSettings { get; set; } = new GlueWeighingSettingSymbols();

        /// <summary>True when the PLC gives any glue figure at all: only then is there a glue data source.</summary>
        public bool IsMapped =>
            (Gantries ?? new Dictionary<string, GlueGantrySymbols>()).Values.Any(g => g != null && g.All().Any(m => PlcSymbolMap.Has(m.Value)))
            || (Weighing != null && Weighing.IsMapped)
            || (Regressions ?? new List<GlueRegressionSymbols>()).Any(r => r != null && r.IsMapped);

        public IEnumerable<KeyValuePair<string, string>> NamedSymbols()
        {
            foreach (var gantry in Gantries ?? new Dictionary<string, GlueGantrySymbols>())
            {
                if (gantry.Value == null)
                    continue;

                foreach (var member in gantry.Value.All())
                    yield return new KeyValuePair<string, string>("Gantries." + gantry.Key + "." + member.Key, member.Value);
            }

            if (Weighing != null)
            {
                yield return new KeyValuePair<string, string>("Weighing.Count", Weighing.Count);
                foreach (var member in Weighing.Members())
                    yield return new KeyValuePair<string, string>("Weighing." + member.Key, member.Value?.Symbol);
                yield return new KeyValuePair<string, string>("Weighing.IsManual", Weighing.IsManual?.Symbol);
            }

            foreach (GlueRegressionSymbols regression in Regressions ?? new List<GlueRegressionSymbols>())
            {
                if (regression == null)
                    continue;

                string key = "Regressions." + regression.Gantry + "." + regression.Path + ".";
                yield return new KeyValuePair<string, string>(key + "W", regression.W);
                yield return new KeyValuePair<string, string>(key + "B", regression.B);
                yield return new KeyValuePair<string, string>(key + "R2", regression.R2);
            }

            if (WeighingSettings != null)
            {
                foreach (var member in WeighingSettings.All())
                    yield return new KeyValuePair<string, string>("WeighingSettings." + member.Key, member.Value);
            }
        }
    }
}
