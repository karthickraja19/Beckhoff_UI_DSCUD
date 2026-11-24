using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Data.Models;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>How a Health tab value stands against its thresholds. Drives the row colour.</summary>
    public enum HealthLevel
    {
        Unknown,
        Normal,
        Warning,
        Critical
    }

    /// <summary>
    /// One glue weighing in the Glue tab's raw data table - S2.4.4.5's columns: Date,
    /// Time, Shift, Gantry, Path, Speed, then Value, LSL, USL and Result under "Glue
    /// weight SPEC".
    /// </summary>
    public sealed class GlueWeighingRow
    {
        public GlueWeighingRow(GlueWeighingRecord record)
        {
            Date = record.Time.ToString("yyyy/M/d");
            Time = record.Time.ToString("HH:mm:ss");
            Shift = record.Shift ?? string.Empty;
            Gantry = record.Gantry ?? string.Empty;
            PathNumberText = record.Path.ToString();
            SpeedText = record.DispenseSpeed > 0 ? record.DispenseSpeed.ToString("0.##") + " mm/s" : "—";
            WeightText = record.WeightMg.ToString("0.000");
            LowerLimitText = record.LowerLimitMg.ToString("0.0##");
            UpperLimitText = record.UpperLimitMg.ToString("0.0##");
            IsFail = !record.IsPass;
            ResultText = record.IsPass ? "Pass" : "Fail";
        }

        public string Date { get; }
        public string Time { get; }
        public string Shift { get; }
        public string Gantry { get; }
        public string PathNumberText { get; }
        public string SpeedText { get; }
        public string WeightText { get; }
        public string LowerLimitText { get; }
        public string UpperLimitText { get; }
        public string ResultText { get; }

        /// <summary>Drives the red row tint, as for a failing unit.</summary>
        public bool IsFail { get; }
    }

    /// <summary>
    /// One row of a Health tab colour key - S2.4.4.4's "Color | Explain and Detail |
    /// Threshold (Minimum, Maximum)" tables beside the nozzle and glue tables.
    /// </summary>
    public sealed class HealthLegendRow
    {
        public HealthLegendRow(HealthLevel level, string detail, string minimum, string maximum)
        {
            Level = level;
            Detail = detail;
            Minimum = minimum;
            Maximum = maximum;
        }

        public HealthLevel Level { get; }
        public string Detail { get; }
        public string Minimum { get; }
        public string Maximum { get; }
    }

    /// <summary>
    /// One gantry's glue cartridge on the Health tab (S2.4.4.4 section 2.1): what has
    /// been used since the glue change, what is left, and how that stands against the
    /// low-glue thresholds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arithmetic is the spec's own. Remaining = cartridge - produced x glue per
    /// product - long purges - short purges - weighings, where one weighing uses the
    /// glue of one finished product. The spec's example gives front 278.24 g, 92.75 %
    /// and 4637 more products; rear 2.69 g, 8.97 % and 449.
    /// </para>
    /// <para>
    /// The share is taken from the grams as displayed (2.69, not 2.694), so the bar's
    /// percentage is the one the table's grams give - and the one the spec prints.
    /// </para>
    /// </remarks>
    public sealed class GlueCartridgeRow
    {
        /// <summary>Guards the floor against a quotient such as 448.99999999 that is really 449.</summary>
        private const double FloorTolerance = 1e-9;

        public GlueCartridgeRow(GlueCartridgeStatus status, GantryGlueDosage dosage, GlueControlSettings settings)
        {
            Gantry = status.Gantry ?? string.Empty;
            GlueSn = string.IsNullOrWhiteSpace(status.GlueSn) ? "—" : status.GlueSn;
            ProducedText = status.ProducedQty.ToString("0");
            LongPurgeCyclesText = status.LongPurgeCycles.ToString("0");
            ShortPurgeCyclesText = status.ShortPurgeCycles.ToString("0");
            WeighingCyclesText = status.WeighingCycles.ToString("0");

            if (dosage == null || dosage.CartridgeWeightG <= 0 || dosage.GluePerProductG <= 0)
            {
                // Without the OSS dosages nothing below can be worked out. Say so rather
                // than divide by zero into a figure that looks real.
                HasDosage = false;
                Level = HealthLevel.Unknown;
                StatusText = "Set the glue dosage for this gantry";
                RemainingPercentText = StatusText;
                CartridgeText = TheoreticalOutputText = GluePerProductText = ProductGlueText =
                    LongPurgeDoseText = LongPurgeTotalText = ShortPurgeDoseText = ShortPurgeTotalText =
                    WeighingTotalText = RemainingText = EstimatedOutputText = "—";
                return;
            }

            HasDosage = true;

            double product = status.ProducedQty * dosage.GluePerProductG;
            double longPurge = status.LongPurgeCycles * dosage.LongPurgeDoseG;
            double shortPurge = status.ShortPurgeCycles * dosage.ShortPurgeDoseG;
            double weighing = status.WeighingCycles * dosage.GluePerProductG;
            double remaining = Math.Max(0, dosage.CartridgeWeightG - product - longPurge - shortPurge - weighing);

            RemainingPercent = Math.Round(remaining, 2) / dosage.CartridgeWeightG * 100d;

            // The spec table's own number formats, column by column.
            CartridgeText = dosage.CartridgeWeightG.ToString("0.00");
            TheoreticalOutputText = Math.Floor(dosage.CartridgeWeightG / dosage.GluePerProductG + FloorTolerance).ToString("0.00");
            GluePerProductText = dosage.GluePerProductG.ToString("0.00##");
            ProductGlueText = product.ToString("0.00");
            LongPurgeDoseText = dosage.LongPurgeDoseG.ToString("0.0000");
            LongPurgeTotalText = longPurge.ToString("0.00000");
            ShortPurgeDoseText = dosage.ShortPurgeDoseG.ToString("0.00000");
            ShortPurgeTotalText = shortPurge.ToString("0.00000");
            WeighingTotalText = weighing.ToString("0.00");
            RemainingText = remaining.ToString("0.00");
            RemainingPercentText = RemainingPercent.ToString("0.00") + "%";
            EstimatedOutputText = Math.Floor(remaining / dosage.GluePerProductG + FloorTolerance).ToString("0");

            if (remaining <= 0)
            {
                Level = HealthLevel.Critical;
                StatusText = "Glue empty - change the cartridge";
            }
            else if (RemainingPercent <= settings.EmptyGluePercent)
            {
                Level = HealthLevel.Critical;
                StatusText = "Change glue now";
            }
            else if (RemainingPercent <= settings.LowGluePercent)
            {
                Level = HealthLevel.Warning;
                StatusText = "Low glue - staging requested";
            }
            else
            {
                Level = HealthLevel.Normal;
                StatusText = "Normal";
            }
        }

        public string Gantry { get; }
        public string GlueSn { get; }
        public bool HasDosage { get; }

        /// <summary>0 to 100.</summary>
        public double RemainingPercent { get; }

        /// <summary>0 to 1, for the gauge bar's two star columns.</summary>
        public double RemainingRatio => Math.Max(0, Math.Min(1, RemainingPercent / 100d));

        public string CartridgeText { get; }
        public string TheoreticalOutputText { get; }
        public string GluePerProductText { get; }
        public string ProducedText { get; }
        public string ProductGlueText { get; }
        public string LongPurgeCyclesText { get; }
        public string LongPurgeDoseText { get; }
        public string LongPurgeTotalText { get; }
        public string ShortPurgeCyclesText { get; }
        public string ShortPurgeDoseText { get; }
        public string ShortPurgeTotalText { get; }
        public string WeighingCyclesText { get; }
        public string WeighingTotalText { get; }
        public string RemainingText { get; }
        public string RemainingPercentText { get; }
        public string EstimatedOutputText { get; }
        public HealthLevel Level { get; }
        public string StatusText { get; }
    }

    /// <summary>
    /// One gantry's glue pot life on the Health tab (S2.4.4.4 section 2.2). MES is
    /// polled for it; at zero the machine stops and HIVE goes to downtime.
    /// </summary>
    public sealed class PotLifeRow
    {
        public PotLifeRow(GlueCartridgeStatus status, double warningHours)
        {
            Gantry = status.Gantry ?? string.Empty;
            GlueSn = string.IsNullOrWhiteSpace(status.GlueSn) ? "—" : status.GlueSn;
            ThresholdText = warningHours.ToString("0.0#");

            if (!status.RemainingPotLifeHours.HasValue)
            {
                RemainingText = "—";
                Level = HealthLevel.Unknown;
                StatusText = "No answer from MES";
                return;
            }

            double hours = status.RemainingPotLifeHours.Value;
            RemainingText = hours.ToString("0.##");

            // The spec's rule: threshold > remaining > 0 is a warning, remaining <= 0
            // stops the machine. Both are red, as S2.4.4.4 draws its 0.4 h "Warning":
            // a pot-life warning is one step from the machine stopping.
            if (hours <= 0)
            {
                Level = HealthLevel.Critical;
                StatusText = "Expired";
            }
            else if (hours < warningHours)
            {
                Level = HealthLevel.Critical;
                StatusText = "Warning";
            }
            else
            {
                Level = HealthLevel.Normal;
                StatusText = "Normal";
            }
        }

        public string Gantry { get; }
        public string GlueSn { get; }
        public string RemainingText { get; }
        public string ThresholdText { get; }
        public HealthLevel Level { get; }
        public string StatusText { get; }
    }

    /// <summary>One nozzle on the Health tab (S2.4.4.4 section 1, thresholds from PD-12).</summary>
    public sealed class NozzleHealthRow
    {
        public NozzleHealthRow(NozzleHealthRecord record, double warningHours, double criticalHours)
        {
            Gantry = record.Gantry ?? string.Empty;
            ValveSn = string.IsNullOrWhiteSpace(record.ValveSn) ? "—" : record.ValveSn;
            NozzleId = string.IsNullOrWhiteSpace(record.NozzleId) ? "—" : record.NozzleId;
            ValueText = record.Value.ToString("0.#");
            LowerLimitText = record.LowerLimit.ToString("0.#");
            UpperLimitText = record.UpperLimit.ToString("0.#");

            // The bar is the value's share of the maintenance period (USL).
            double span = record.UpperLimit - record.LowerLimit;
            Ratio = span > 0 ? Math.Max(0, Math.Min(1, (record.Value - record.LowerLimit) / span)) : 0;

            // S2.4.4.4's key: red 0 to 12 h, yellow 12 to 24 h, green from 24 h - and it
            // draws its 12 h rear nozzle red, so the critical level itself is red.
            if (record.Value <= criticalHours)
            {
                Level = HealthLevel.Critical;
                StatusText = "Critical";
            }
            else if (record.Value < warningHours)
            {
                Level = HealthLevel.Warning;
                StatusText = "Warning";
            }
            else
            {
                Level = HealthLevel.Normal;
                StatusText = "Normal";
            }
        }

        public string Gantry { get; }
        public string ValveSn { get; }
        public string NozzleId { get; }
        public string ValueText { get; }
        public string LowerLimitText { get; }
        public string UpperLimitText { get; }

        /// <summary>0 to 1, for the health bar's two star columns.</summary>
        public double Ratio { get; }
        public HealthLevel Level { get; }
        public string StatusText { get; }
    }

    /// <summary>
    /// One gantry's glue dosages in the Health tab's settings card, edited as text and
    /// checked on Save - the same approach as the Config page's spec limits.
    /// </summary>
    public partial class GantryDosageRow : ObservableObject
    {
        public GantryDosageRow(GantryGlueDosage dosage)
        {
            Gantry = dosage.Gantry ?? string.Empty;
            _cartridgeWeight = Format(dosage.CartridgeWeightG);
            _gluePerProduct = Format(dosage.GluePerProductG);
            _longPurgeDose = Format(dosage.LongPurgeDoseG);
            _shortPurgeDose = Format(dosage.ShortPurgeDoseG);
        }

        public string Gantry { get; }

        [ObservableProperty]
        private string _cartridgeWeight;

        [ObservableProperty]
        private string _gluePerProduct;

        [ObservableProperty]
        private string _longPurgeDose;

        [ObservableProperty]
        private string _shortPurgeDose;

        /// <summary>Why Save refused this row, or null.</summary>
        [ObservableProperty]
        private string _error;

        /// <summary>Parses the row. Sets <see cref="Error"/> and returns false when it cannot be saved.</summary>
        public bool TryBuild(out GantryGlueDosage dosage)
        {
            dosage = null;
            double cartridge, perProduct, longDose, shortDose;

            if (!TryParsePositive(CartridgeWeight, out cartridge))
                Error = "Cartridge weight must be a number above 0.";
            else if (!TryParsePositive(GluePerProduct, out perProduct))
                Error = "Glue per product must be a number above 0.";
            else if (!TryParseNonNegative(LongPurgeDose, out longDose))
                Error = "Long purge dose must be a number, 0 or more.";
            else if (!TryParseNonNegative(ShortPurgeDose, out shortDose))
                Error = "Short purge dose must be a number, 0 or more.";
            else if (perProduct > cartridge)
                Error = "Glue per product is more than the cartridge holds.";
            else
            {
                Error = null;
                dosage = new GantryGlueDosage
                {
                    Gantry = Gantry,
                    CartridgeWeightG = cartridge,
                    GluePerProductG = perProduct,
                    LongPurgeDoseG = longDose,
                    ShortPurgeDoseG = shortDose
                };
            }

            return Error == null;
        }

        internal static string Format(double value)
        {
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        internal static bool TryParseNonNegative(string text, out double value)
        {
            return double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   && !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
        }

        internal static bool TryParsePositive(string text, out double value)
        {
            return TryParseNonNegative(text, out value) && value > 0;
        }
    }
}
