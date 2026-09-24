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

    /// <summary>One glue weighing in the Glue tab's raw data table (S2.4.4.5).</summary>
    public sealed class GlueWeighingRow
    {
        public GlueWeighingRow(GlueWeighingRecord record)
        {
            Date = record.Time.ToString("yyyy/MM/dd");
            Time = record.Time.ToString("HH:mm:ss");
            DateTimeText = record.Time.ToString("MM/dd HH:mm:ss");
            PathNumberText = record.Path.ToString();
            Shift = record.Shift ?? string.Empty;
            Gantry = record.Gantry ?? string.Empty;
            PathText = "Path " + record.Path;
            WeightText = record.WeightMg.ToString("0.000");
            LowerLimitText = record.LowerLimitMg.ToString("0.000");
            UpperLimitText = record.UpperLimitMg.ToString("0.000");
            IsFail = !record.IsPass;
            ResultText = record.IsPass ? "Pass" : "Fail";
        }

        public string Date { get; }
        public string Time { get; }

        /// <summary>"09/24 08:05:12" - one column, so the table fits the Glue tab's half width.</summary>
        public string DateTimeText { get; }
        public string Shift { get; }
        public string Gantry { get; }
        public string PathText { get; }
        public string PathNumberText { get; }
        public string WeightText { get; }
        public string LowerLimitText { get; }
        public string UpperLimitText { get; }
        public string ResultText { get; }

        /// <summary>Drives the red row tint, as for a failing unit.</summary>
        public bool IsFail { get; }
    }

    /// <summary>One gantry's glue regression in the Glue tab (HIVE keys w, b, R2).</summary>
    public sealed class GlueRegressionRow
    {
        public GlueRegressionRow(GlueRegression regression)
        {
            Gantry = regression.Gantry ?? string.Empty;
            WText = regression.W.ToString("0.00000");
            BText = regression.B.ToString("0.00000");
            R2Text = regression.R2.ToString("0.0000");
            CalibratedText = regression.CalibratedAt.HasValue
                ? regression.CalibratedAt.Value.ToString("yyyy/MM/dd HH:mm")
                : "—";
            Formula = "y = " + regression.W.ToString("0.00000") + " x "
                      + (regression.B < 0 ? "- " : "+ ") + Math.Abs(regression.B).ToString("0.00000");
        }

        public string Gantry { get; }
        public string WText { get; }
        public string BText { get; }
        public string R2Text { get; }
        public string CalibratedText { get; }
        public string Formula { get; }
    }

    /// <summary>
    /// One gantry's glue cartridge on the Health tab (S2.4.4.4 section 2.1): what has
    /// been used since the glue change, what is left, and how that stands against the
    /// low-glue thresholds.
    /// </summary>
    /// <remarks>
    /// The arithmetic is the spec's own. Remaining = cartridge - produced x glue per
    /// product - long purges - short purges - weighings, where one weighing uses the
    /// glue of one finished product. With the spec's front-gantry example (300 g,
    /// 0.06 g, 346 made, 9 x 0.02, 70 x 0.01, 2 weighings) this gives 278.24 g, 92.75 %
    /// and 4637 more products, as the spec shows.
    /// </remarks>
    public sealed class GlueCartridgeRow
    {
        public GlueCartridgeRow(GlueCartridgeStatus status, GantryGlueDosage dosage, GlueControlSettings settings)
        {
            Gantry = status.Gantry ?? string.Empty;
            GlueSn = string.IsNullOrWhiteSpace(status.GlueSn) ? "—" : status.GlueSn;
            ChangedText = status.ChangedAt.HasValue ? status.ChangedAt.Value.ToString("MM/dd HH:mm") : "—";
            ProducedText = status.ProducedQty.ToString("N0");
            LongPurgeCyclesText = status.LongPurgeCycles.ToString("N0");
            ShortPurgeCyclesText = status.ShortPurgeCycles.ToString("N0");
            WeighingCyclesText = status.WeighingCycles.ToString("N0");

            if (dosage == null || dosage.CartridgeWeightG <= 0 || dosage.GluePerProductG <= 0)
            {
                // Without the OSS dosages nothing below can be worked out. Say so rather
                // than divide by zero into a figure that looks real.
                HasDosage = false;
                Level = HealthLevel.Unknown;
                StatusText = "Set the glue dosage for this gantry";
                CartridgeText = TheoreticalOutputText = GluePerProductText = ProductGlueText =
                    LongPurgeText = ShortPurgeText = WeighingText = RemainingText =
                    RemainingPercentText = EstimatedOutputText = UsedText = UsedBreakdown = "—";
                return;
            }

            HasDosage = true;

            double product = status.ProducedQty * dosage.GluePerProductG;
            double longPurge = status.LongPurgeCycles * dosage.LongPurgeDoseG;
            double shortPurge = status.ShortPurgeCycles * dosage.ShortPurgeDoseG;
            double weighing = status.WeighingCycles * dosage.GluePerProductG;
            double remaining = Math.Max(0, dosage.CartridgeWeightG - product - longPurge - shortPurge - weighing);

            RemainingPercent = remaining / dosage.CartridgeWeightG * 100d;

            CartridgeText = dosage.CartridgeWeightG.ToString("0.00");
            TheoreticalOutputText = Math.Floor(dosage.CartridgeWeightG / dosage.GluePerProductG).ToString("N0");
            GluePerProductText = dosage.GluePerProductG.ToString("0.0000");
            ProductGlueText = product.ToString("0.00");
            LongPurgeText = status.LongPurgeCycles + " x " + dosage.LongPurgeDoseG.ToString("0.0000") + " = " + longPurge.ToString("0.000");
            ShortPurgeText = status.ShortPurgeCycles + " x " + dosage.ShortPurgeDoseG.ToString("0.0000") + " = " + shortPurge.ToString("0.000");
            WeighingText = status.WeighingCycles + " x " + dosage.GluePerProductG.ToString("0.0000") + " = " + weighing.ToString("0.000");
            RemainingText = remaining.ToString("0.00");
            RemainingPercentText = RemainingPercent.ToString("0.00") + "%";
            UsedText = (product + longPurge + shortPurge + weighing).ToString("0.00");
            UsedBreakdown = "Products " + ProductGlueText + " g + long purge " + LongPurgeText
                            + " g + short purge " + ShortPurgeText + " g + weighing " + WeighingText + " g";
            EstimatedOutputText = Math.Floor(remaining / dosage.GluePerProductG).ToString("N0");

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
        public string ChangedText { get; }
        public bool HasDosage { get; }

        /// <summary>0 to 100.</summary>
        public double RemainingPercent { get; }

        /// <summary>0 to 1, for the gauge bar's two star columns.</summary>
        public double RemainingRatio => Math.Max(0, Math.Min(1, RemainingPercent / 100d));

        public string CartridgeText { get; }
        public string TheoreticalOutputText { get; }
        public string GluePerProductText { get; }
        public string ProducedText { get; }
        public string LongPurgeCyclesText { get; }
        public string ShortPurgeCyclesText { get; }
        public string WeighingCyclesText { get; }

        /// <summary>Everything taken from the cartridge since the glue change, in grams.</summary>
        public string UsedText { get; }

        /// <summary>How <see cref="UsedText"/> adds up, for its tooltip.</summary>
        public string UsedBreakdown { get; }
        public string ProductGlueText { get; }
        public string LongPurgeText { get; }
        public string ShortPurgeText { get; }
        public string WeighingText { get; }
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
            RemainingText = hours.ToString("0.0#");

            // The spec's rule: threshold > remaining > 0 is a warning, remaining <= 0
            // stops the machine.
            if (hours <= 0)
            {
                Level = HealthLevel.Critical;
                StatusText = "Expired - machine stops";
            }
            else if (hours < warningHours)
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

            if (record.Value < criticalHours)
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
