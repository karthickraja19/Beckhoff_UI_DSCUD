using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Nova.Abstractions.Data.Repositories;
using Nova.ViewModels.Audio;
using SkiaSharp;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The I/O, Tossing and Yield tabs (slides 59 and 60, S2.4.4.3), the unit table and
    /// the summary column.
    /// </summary>
    public partial class AudioDataViewModel
    {
        /// <summary>
        /// The unit table is a viewer, not an export: a full week of units would push
        /// tens of thousands of rows into the visual tree. Export writes all of them.
        /// </summary>
        private const int MaxTableRows = 500;

        /// <summary>HIVE's ceiling for output_ct (slide 15).</summary>
        private const double MaxOutputCtSeconds = 3600;

        private IReadOnlyList<ProductionUnitRecord> _records = Empty<ProductionUnitRecord>();

        /// <summary>output_ct per record: this unit's output time minus the previous unit's.</summary>
        private Dictionary<ProductionUnitRecord, double> _outputCt = new Dictionary<ProductionUnitRecord, double>();


        // ==================================================================
        // Charts
        // ==================================================================

        /// <summary>Top chart. I/O: output CT per shift. Tossing: tossing rate. Yield: yield % and target.</summary>
        [ObservableProperty]
        private ISeries[] _topSeries = new ISeries[0];

        /// <summary>Bottom chart. I/O: output per shift. Tossing: tossed units. Yield: pass and fail.</summary>
        [ObservableProperty]
        private ISeries[] _bottomSeries = new ISeries[0];

        [ObservableProperty]
        private Axis[] _sharedXAxes = new Axis[0];

        [ObservableProperty]
        private Axis[] _topYAxes = new Axis[0];

        [ObservableProperty]
        private Axis[] _bottomYAxes = new Axis[0];

        public string TopChartTitle =>
            IsTossingView ? "Tossing rate (%) by shift"
            : IsYieldView ? "Yield (%) by shift"
            : "Output CT (s) by shift";

        public string BottomChartTitle =>
            IsTossingView ? "Tossed units by shift"
            : IsYieldView ? "Pass / Fail units"
            : "Output units by shift";


        // ==================================================================
        // Unit table and summary column
        // ==================================================================

        public ObservableCollection<UnitDataRow> Units { get; } = new ObservableCollection<UnitDataRow>();

        [ObservableProperty]
        private string _lastUnitSn = "—";

        [ObservableProperty]
        private bool _lastUnitPassed = true;

        public ObservableCollection<SummaryRow> IoSummary { get; } = new ObservableCollection<SummaryRow>();

        public ObservableCollection<SummaryRow> TossingSummary { get; } = new ObservableCollection<SummaryRow>();


        // ==================================================================
        // Loading
        // ==================================================================

        private async Task LoadProductionAsync()
        {
            if (_production == null)
            {
                _records = Empty<ProductionUnitRecord>();
                ProjectProduction();
                ProductionStatus = "No production record is configured on this machine.";
                return;
            }

            try
            {
                _records = await _production.GetProductionRecordsAsync(StartTime, EndTime)
                           ?? Empty<ProductionUnitRecord>();

                string window = StartTime.ToString("dd/MM/yyyy HH:mm") + " to " + EndTime.ToString("dd/MM/yyyy HH:mm");
                ProductionStatus = _records.Count == 0
                    ? "No units from " + window + "."
                    : _records.Count.ToString("N0") + " units from " + window + "."
                      + (_records.Count > MaxTableRows
                          ? " The table shows the newest " + MaxTableRows.ToString("N0") + "; Export writes all."
                          : string.Empty);
            }
            catch (Exception ex)
            {
                _records = Empty<ProductionUnitRecord>();
                ProductionStatus = "Query failed: " + ex.Message;
            }

            ProjectProduction();
            ExportCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Re-derives the charts, the unit table and both summaries from
        /// <see cref="_records"/>. Called on a new query and on a tab switch.
        /// </summary>
        private void ProjectProduction()
        {
            _outputCt = ComputeOutputCt(_records);

            BuildUnitTable();
            BuildProductionCharts();
            BuildSummaries();

            OnPropertyChanged(nameof(TopChartTitle));
            OnPropertyChanged(nameof(BottomChartTitle));
        }

        /// <summary>
        /// output_ct as slide 15 defines it: a unit's output time minus the previous
        /// unit's, capped at 3600 s. The first unit has no previous one; HIVE gets 0
        /// for it, and it is left out of the averages here so a 0 does not pull them down.
        /// </summary>
        private static Dictionary<ProductionUnitRecord, double> ComputeOutputCt(IReadOnlyList<ProductionUnitRecord> records)
        {
            var result = new Dictionary<ProductionUnitRecord, double>();
            ProductionUnitRecord previous = null;

            foreach (ProductionUnitRecord record in records.OrderBy(r => r.OutputTime))
            {
                if (previous != null)
                    result[record] = Math.Min(MaxOutputCtSeconds, (record.OutputTime - previous.OutputTime).TotalSeconds);

                previous = record;
            }

            return result;
        }

        private void BuildUnitTable()
        {
            Units.Clear();

            foreach (ProductionUnitRecord record in _records
                         .OrderByDescending(r => r.ProductionTime)
                         .Take(MaxTableRows))
            {
                Units.Add(new UnitDataRow(
                    record.Date.ToString("yyyy/MM/dd"),
                    record.Shift,
                    record.Gantry,
                    record.ProductCode,
                    record.ComponentSn,
                    record.ProductionTime.ToString("HH:mm:ss.fff"),
                    record.OutputTime.ToString("HH:mm:ss.fff"),
                    record.IsPass,
                    record.CycleTimeSeconds,
                    record.HiveState,
                    record.TossingCount));
            }

            ProductionUnitRecord newest = _records.OrderByDescending(r => r.ProductionTime).FirstOrDefault();
            LastUnitSn = newest != null && !string.IsNullOrWhiteSpace(newest.ProductCode) ? newest.ProductCode : "—";
            LastUnitPassed = newest == null || newest.IsPass;
        }


        // ==================================================================
        // Charts
        // ==================================================================

        /// <summary>
        /// Every hour (or day) in the window gets a column, including empty ones, so an
        /// idle hour shows as a gap instead of the axis closing it up.
        /// </summary>
        private List<DateTime> BucketStarts()
        {
            var starts = new List<DateTime>();

            DateTime first = IsDaily
                ? StartTime.Date
                : new DateTime(StartTime.Year, StartTime.Month, StartTime.Day, StartTime.Hour, 0, 0);

            // A window of a few months hourly would be thousands of columns; the charts
            // stop at a thousand buckets.
            for (DateTime t = first; t <= EndTime && starts.Count < 1000; t = IsDaily ? t.AddDays(1) : t.AddHours(1))
                starts.Add(t);

            return starts;
        }

        /// <summary>
        /// Hourly buckets go by the unit's own time; daily ones by its production date,
        /// so a night-shift unit made after midnight counts towards its shift's day - as
        /// slide 59's daily chart pairs DS and NS on one date.
        /// </summary>
        private DateTime BucketOf(ProductionUnitRecord record)
        {
            return IsDaily
                ? record.Date.Date
                : new DateTime(record.ProductionTime.Year, record.ProductionTime.Month, record.ProductionTime.Day,
                               record.ProductionTime.Hour, 0, 0);
        }

        /// <summary>The shifts in the data, DS before NS as the slides draw them, others after.</summary>
        private List<string> ShiftsInData()
        {
            return _records
                .Select(r => string.IsNullOrWhiteSpace(r.Shift) ? "Unassigned" : r.Shift.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s.Equals("DS", StringComparison.OrdinalIgnoreCase) ? 0
                              : s.Equals("NS", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .ThenBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ShiftOf(ProductionUnitRecord record)
        {
            return string.IsNullOrWhiteSpace(record.Shift) ? "Unassigned" : record.Shift.Trim();
        }

        private static SKColor ShiftColour(int index)
        {
            if (index == 0)
                return AudioChartPalette.SeriesPrimary;

            return index == 1 ? AudioChartPalette.SeriesSecondary : AudioChartPalette.TopN(index);
        }

        private void BuildProductionCharts()
        {
            List<DateTime> buckets = BucketStarts();
            List<string> shifts = ShiftsInData();

            // bucket -> shift -> records
            var grouped = _records
                .GroupBy(BucketOf)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(ShiftOf, StringComparer.OrdinalIgnoreCase)
                          .ToDictionary(s => s.Key, s => s.ToList(), StringComparer.OrdinalIgnoreCase));

            Func<DateTime, string, List<ProductionUnitRecord>> cell = (bucket, shift) =>
            {
                Dictionary<string, List<ProductionUnitRecord>> byShift;
                List<ProductionUnitRecord> list;
                return grouped.TryGetValue(bucket, out byShift) && byShift.TryGetValue(shift, out list)
                    ? list
                    : new List<ProductionUnitRecord>();
            };

            var top = new List<ISeries>();
            var bottom = new List<ISeries>();

            for (int i = 0; i < shifts.Count; i++)
            {
                string shift = shifts[i];
                SKColor colour = ShiftColour(i);

                double?[] rate = buckets.Select(b => TopValue(cell(b, shift))).ToArray();
                top.Add(ShiftLine(shift + " " + TopSeriesSuffix(), rate, colour));

                if (!IsYieldView)
                {
                    double[] volume = buckets.Select(b => BottomValue(cell(b, shift))).ToArray();
                    bottom.Add(new ColumnSeries<double>
                    {
                        Name = shift + (IsTossingView ? " tossed" : " output"),
                        Values = volume,
                        Fill = AudioChartPalette.Fill(colour),
                        Stroke = null,
                        MaxBarWidth = 28
                    });
                }
            }

            if (IsYieldView)
            {
                if (TargetYieldPercent.HasValue)
                {
                    top.Add(new LineSeries<double>
                    {
                        Name = "Target yield",
                        Values = buckets.Select(_ => TargetYieldPercent.Value).ToArray(),
                        Stroke = DashedLine(AudioChartPalette.Downtime),
                        Fill = null,
                        GeometrySize = 0,
                        LineSmoothness = 0
                    });
                }

                // Pass stacked under fail, every shift together: one bar per bucket.
                List<ProductionUnitRecord>[] all = buckets
                    .Select(b => shifts.SelectMany(s => cell(b, s)).ToList())
                    .ToArray();

                bottom.Add(new StackedColumnSeries<double>
                {
                    Name = "Pass",
                    Values = all.Select(l => (double)l.Count(r => r.IsPass)).ToArray(),
                    Fill = AudioChartPalette.Fill(AudioChartPalette.SeriesPrimary),
                    Stroke = null,
                    MaxBarWidth = 28
                });
                bottom.Add(new StackedColumnSeries<double>
                {
                    Name = "Fail",
                    Values = all.Select(l => (double)l.Count(r => !r.IsPass)).ToArray(),
                    Fill = AudioChartPalette.Fill(AudioChartPalette.Downtime),
                    Stroke = null,
                    MaxBarWidth = 28
                });
            }

            TopSeries = top.ToArray();
            BottomSeries = bottom.ToArray();

            var labels = buckets
                .Select(b => IsDaily ? b.ToString("yyyy/MM/dd") : b.ToString("MM/dd HH:00"))
                .ToList();

            SharedXAxes = new[]
            {
                new Axis
                {
                    Labels = labels,
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 11,
                    LabelsRotation = labels.Count > 12 ? 45 : 0,
                    SeparatorsPaint = null
                }
            };

            // Yield sits in the high 90s and tossing in the low single digits, so neither
            // is pinned to 0-100: that would flatten the very movement the chart is for.
            // Yield stops at 100 and scales its floor; the others start at 0.
            TopYAxes = new[]
            {
                new Axis
                {
                    MinLimit = IsYieldView ? (double?)null : 0,
                    MaxLimit = IsYieldView ? (double?)100 : null,
                    Labeler = value => IsTossingView || IsYieldView ? value.ToString("0") + "%" : value.ToString("0.0"),
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 11,
                    SeparatorsPaint = AudioChartPalette.Line(AudioChartPalette.GridLine, 1)
                }
            };

            BottomYAxes = new[]
            {
                new Axis
                {
                    MinLimit = 0,
                    MinStep = 1,
                    Labeler = value => value.ToString("0"),
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 11,
                    SeparatorsPaint = AudioChartPalette.Line(AudioChartPalette.GridLine, 1)
                }
            };
        }

        private string TopSeriesSuffix()
        {
            return IsTossingView ? "tossing rate" : IsYieldView ? "yield" : "output CT";
        }

        /// <summary>
        /// The top chart's value for one bucket and shift, or null when nothing was made
        /// there - a gap, not a zero, because "no units" is not "0 % yield".
        /// </summary>
        private double? TopValue(List<ProductionUnitRecord> records)
        {
            if (records.Count == 0)
                return null;

            if (IsTossingView)
                return TossingRate(records);

            if (IsYieldView)
                return (double)records.Count(r => r.IsPass) / records.Count * 100d;

            double sum = 0;
            int n = 0;
            foreach (ProductionUnitRecord record in records)
            {
                double ct;
                if (_outputCt.TryGetValue(record, out ct))
                {
                    sum += ct;
                    n++;
                }
            }

            return n > 0 ? sum / n : (double?)null;
        }

        private double BottomValue(List<ProductionUnitRecord> records)
        {
            return IsTossingView ? records.Sum(r => r.TossingCount) : records.Count;
        }

        /// <summary>
        /// Tossing events over input attempts - slide 26's rate, where every record is
        /// one attempt and a tossed attempt carries its tossing count.
        /// </summary>
        private static double TossingRate(IReadOnlyCollection<ProductionUnitRecord> records)
        {
            return records.Count == 0 ? 0 : (double)records.Sum(r => r.TossingCount) / records.Count * 100d;
        }

        private static LineSeries<double?> ShiftLine(string name, double?[] values, SKColor colour)
        {
            return new LineSeries<double?>
            {
                Name = name,
                Values = values,
                Stroke = AudioChartPalette.Line(colour, 2),
                Fill = null,
                GeometrySize = 6,
                GeometryStroke = AudioChartPalette.Line(colour, 2),
                GeometryFill = AudioChartPalette.Fill(AudioChartPalette.Stroke),
                LineSmoothness = 0
            };
        }

        private static SolidColorPaint DashedLine(SKColor colour)
        {
            return new SolidColorPaint(colour)
            {
                StrokeThickness = 1.5f,
                PathEffect = new DashEffect(new float[] { 6, 4 })
            };
        }


        // ==================================================================
        // Summary column (slides 59, 60)
        // ==================================================================

        /// <remarks>
        /// Rebuilt wholesale rather than updated in place, because these only change on
        /// a query or a tab switch - both on the UI thread.
        /// </remarks>
        private void BuildSummaries()
        {
            int total = _records.Count;
            int pass = _records.Count(r => r.IsPass);
            int fail = total - pass;

            double hours = (EndTime - StartTime).TotalHours;
            double uph = hours > 0 ? total / hours : 0;
            double cycle = total > 0 ? _records.Average(r => r.CycleTimeSeconds) : 0;
            double outputCt = _outputCt.Count > 0 ? _outputCt.Values.Average() : 0;
            double yield = total > 0 ? (double)pass / total * 100d : 0;

            IoSummary.Clear();
            IoSummary.Add(new SummaryRow("Input/Output", total.ToString("N0") + "/" + pass.ToString("N0")));
            IoSummary.Add(new SummaryRow("Yield", yield.ToString("0.0") + "%"));

            if (IsYieldView)
            {
                IoSummary.Add(new SummaryRow("Target",
                    TargetYieldPercent.HasValue ? TargetYieldPercent.Value.ToString("0.0") + "%" : "not set"));
            }

            IoSummary.Add(new SummaryRow("Pass/Fail", pass.ToString("N0") + "/" + fail.ToString("N0")));
            IoSummary.Add(new SummaryRow("UPH", uph.ToString("N0")));
            IoSummary.Add(new SummaryRow("CT", cycle.ToString("0.00") + " s"));
            IoSummary.Add(new SummaryRow("Output CT", outputCt.ToString("0.00") + " s"));

            // A D-SCUD tosses a unit when its pre-process CCD check fails (slide 23), so
            // the causes are the reasons the machine gave - not slide 60's PnP pick-up
            // causes, which this machine never reports.
            int tossed = _records.Sum(r => r.TossingCount);

            TossingSummary.Clear();
            TossingSummary.Add(new SummaryRow("Tossed units", tossed.ToString("N0")));
            TossingSummary.Add(new SummaryRow("Tossing rate", TossingRate(_records).ToString("0.00") + "%"));

            foreach (var reason in _records
                         .Where(r => r.TossingCount > 0)
                         .GroupBy(r => string.IsNullOrWhiteSpace(r.TossingReason) ? "Reason not recorded" : r.TossingReason)
                         .OrderByDescending(g => g.Sum(r => r.TossingCount)))
            {
                TossingSummary.Add(new SummaryRow(reason.Key, reason.Sum(r => r.TossingCount).ToString("N0")));
            }

            foreach (var gantry in _records
                         .Where(r => !string.IsNullOrWhiteSpace(r.Gantry))
                         .GroupBy(r => r.Gantry, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key.Equals("Front", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            {
                TossingSummary.Add(new SummaryRow(gantry.Key + " gantry",
                    gantry.Sum(r => r.TossingCount).ToString("N0") + " (" + TossingRate(gantry.ToList()).ToString("0.00") + "%)"));
            }
        }


        // ==================================================================
        // Export
        // ==================================================================

        private static IReadOnlyList<string> UnitExportHeaders()
        {
            return new[]
            {
                "Date", "Shift", "Gantry", "Unit SN", "Component SN", "Start Time", "End Time",
                "Pass", "CT/s", "Output CT/s", "HIVE State", "Tossing", "Tossing Reason", "Failure Code"
            };
        }

        private IReadOnlyList<IReadOnlyList<string>> UnitExportRows()
        {
            return _records
                .OrderByDescending(r => r.ProductionTime)
                .Select(r =>
                {
                    double ct;
                    bool hasCt = _outputCt.TryGetValue(r, out ct);
                    return (IReadOnlyList<string>)new[]
                    {
                        r.Date.ToString("yyyy/MM/dd"),
                        r.Shift ?? string.Empty,
                        r.Gantry ?? string.Empty,
                        r.ProductCode ?? string.Empty,
                        r.ComponentSn ?? string.Empty,
                        r.ProductionTime.ToString("yyyy/MM/dd HH:mm:ss.fff"),
                        r.OutputTime.ToString("yyyy/MM/dd HH:mm:ss.fff"),
                        r.IsPass ? "TRUE" : "FALSE",
                        r.CycleTimeSeconds.ToString("0.00"),
                        hasCt ? ct.ToString("0.00") : "0",
                        r.HiveState.HasValue ? r.HiveState.Value.ToString() : string.Empty,
                        r.TossingCount.ToString(),
                        r.TossingReason ?? string.Empty,
                        r.FailureCode ?? string.Empty
                    };
                })
                .ToList();
        }
    }
}
