using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using Nova.Abstractions.Services;
using Nova.ViewModels.Audio;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>One coordinate in the Axis tab's table.</summary>
    public sealed class CoordinateRow
    {
        public CoordinateRow(CoordinateRecord record)
        {
            Time = record.Time.ToString("yyyy-MM-dd HH:mm:ss");
            UnitSn = record.UnitSn;
            Point = record.Point;
            Axis = record.Axis;
            Value = record.Value.ToString("0.000", CultureInfo.InvariantCulture);
            IsNg = (record.Lsl.HasValue && record.Value < record.Lsl.Value) || (record.Usl.HasValue && record.Value > record.Usl.Value);
            Result = IsNg ? "NG" : "OK";
        }

        public string Time { get; }
        public string UnitSn { get; }
        public string Point { get; }
        public string Axis { get; }
        public string Value { get; }
        public string Result { get; }
        public bool IsNg { get; }
    }

    /// <summary>
    /// The Axis tab - S2.4.4.6 Coordinate Data Analysis: the coordinates of one point on
    /// one axis over the time window, as a table with its OK/NG against the limits, the
    /// CPK, and the trend of every measurement against LSL, nominal and USL.
    /// </summary>
    public partial class AudioDataViewModel
    {
        private const string AxisTab = "Axis";

        private readonly ICoordinateDataSource _coordinates;
        private IReadOnlyList<CoordinateRecord> _axisRecords = new CoordinateRecord[0];
        private List<CoordinateRecord> _axisSelection = new List<CoordinateRecord>();

        public bool IsAxisView => SelectedView == AxisTab;

        public ObservableCollection<string> AxisPoints { get; } = new ObservableCollection<string>();

        public ObservableCollection<string> AxisNames { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        private string _selectedAxisPoint;

        [ObservableProperty]
        private string _selectedAxisName;

        partial void OnSelectedAxisPointChanged(string value) => BuildAxis();

        partial void OnSelectedAxisNameChanged(string value) => BuildAxis();

        public ObservableCollection<CoordinateRow> AxisRows { get; } = new ObservableCollection<CoordinateRow>();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        private string _axisStatus = string.Empty;

        [ObservableProperty]
        private string _axisCount = "-";

        [ObservableProperty]
        private string _axisMean = "-";

        [ObservableProperty]
        private string _axisSigma = "-";

        [ObservableProperty]
        private string _axisRange = "-";

        [ObservableProperty]
        private string _axisLimits = "-";

        [ObservableProperty]
        private string _axisCpk = "-";

        [ObservableProperty]
        private string _axisNg = "-";

        [ObservableProperty]
        private bool _hasAxisData;

        [ObservableProperty]
        private ISeries[] _axisSeries = new ISeries[0];

        [ObservableProperty]
        private Axis[] _axisXAxes = new Axis[0];

        [ObservableProperty]
        private Axis[] _axisYAxes = new Axis[0];

        private async Task LoadAxisAsync()
        {
            if (_coordinates == null)
            {
                _axisRecords = new CoordinateRecord[0];
                AxisStatus = "The machine does not report measured coordinates yet, so there are none to show. "
                             + "They are recorded for each finished unit once the PLC provides them.";
                FillAxisFilters();
                return;
            }

            try
            {
                _axisRecords = await _coordinates.QueryAsync(StartTime, EndTime) ?? new CoordinateRecord[0];
                AxisStatus = _axisRecords.Count == 0
                    ? "No coordinates were measured in this window."
                    : _axisRecords.Select(r => r.UnitSn).Distinct().Count() + " units, " + _axisRecords.Count + " coordinates.";
            }
            catch (Exception ex)
            {
                _axisRecords = new CoordinateRecord[0];
                AxisStatus = "Coordinate data could not be read: " + ex.Message;
            }

            FillAxisFilters();
        }

        /// <summary>The Point Name and Axis choices come from the data itself.</summary>
        private void FillAxisFilters()
        {
            string point = SelectedAxisPoint;
            string axis = SelectedAxisName;

            AxisPoints.Clear();
            foreach (string p in _axisRecords.Select(r => r.Point).Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                AxisPoints.Add(p);

            AxisNames.Clear();
            foreach (string a in _axisRecords.Select(r => r.Axis).Distinct().OrderBy(a => a, StringComparer.OrdinalIgnoreCase))
                AxisNames.Add(a);

            // Setting both re-builds twice; the second build is the one that counts.
            SelectedAxisPoint = AxisPoints.Contains(point) ? point : AxisPoints.FirstOrDefault();
            SelectedAxisName = AxisNames.Contains(axis) ? axis : AxisNames.FirstOrDefault();
            BuildAxis();
        }

        private void BuildAxis()
        {
            _axisSelection = _axisRecords
                .Where(r => r.Point == SelectedAxisPoint && r.Axis == SelectedAxisName)
                .OrderBy(r => r.Time)
                .ToList();

            AxisRows.Clear();
            foreach (CoordinateRecord record in Enumerable.Reverse(_axisSelection))
                AxisRows.Add(new CoordinateRow(record));

            HasAxisData = _axisSelection.Count > 0;
            ExportCommand.NotifyCanExecuteChanged();

            if (_axisSelection.Count == 0)
            {
                AxisCount = AxisMean = AxisSigma = AxisRange = AxisLimits = AxisCpk = AxisNg = "-";
                AxisSeries = new ISeries[0];
                AxisXAxes = new Axis[0];
                AxisYAxes = new Axis[0];
                return;
            }

            List<double> values = _axisSelection.Select(r => r.Value).ToList();
            double mean = values.Average();
            double sigma = values.Count > 1 ? Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1)) : 0;
            CoordinateRecord latest = _axisSelection[_axisSelection.Count - 1];
            double? lsl = latest.Lsl, usl = latest.Usl, nominal = latest.Nominal;
            int ng = AxisRows.Count(r => r.IsNg);

            AxisCount = values.Count.ToString(CultureInfo.InvariantCulture);
            AxisMean = mean.ToString("0.0000", CultureInfo.InvariantCulture);
            AxisSigma = sigma.ToString("0.00000", CultureInfo.InvariantCulture);
            AxisRange = values.Min().ToString("0.000", CultureInfo.InvariantCulture) + " to " + values.Max().ToString("0.000", CultureInfo.InvariantCulture);
            AxisLimits = (lsl.HasValue ? lsl.Value.ToString("0.000", CultureInfo.InvariantCulture) : "-") + " / "
                         + (nominal.HasValue ? nominal.Value.ToString("0.000", CultureInfo.InvariantCulture) : "-") + " / "
                         + (usl.HasValue ? usl.Value.ToString("0.000", CultureInfo.InvariantCulture) : "-");
            AxisNg = ng + " of " + values.Count;

            // CPK = min(USL - mean, mean - LSL) / 3 sigma; one-sided when only one limit is set.
            double? cpk = null;
            if (sigma > 0)
            {
                double? upper = usl.HasValue ? (usl.Value - mean) / (3 * sigma) : (double?)null;
                double? lower = lsl.HasValue ? (mean - lsl.Value) / (3 * sigma) : (double?)null;
                cpk = upper.HasValue && lower.HasValue ? Math.Min(upper.Value, lower.Value) : upper ?? lower;
            }

            AxisCpk = cpk.HasValue ? cpk.Value.ToString("0.000", CultureInfo.InvariantCulture) : "-";

            BuildAxisChart(values, lsl, nominal, usl);
        }

        private void BuildAxisChart(List<double> values, double? lsl, double? nominal, double? usl)
        {
            var points = new List<ObservablePoint>();
            var outOfSpec = new List<ObservablePoint>();
            for (int i = 0; i < values.Count; i++)
            {
                points.Add(new ObservablePoint(i + 1, values[i]));
                if ((lsl.HasValue && values[i] < lsl.Value) || (usl.HasValue && values[i] > usl.Value))
                    outOfSpec.Add(new ObservablePoint(i + 1, values[i]));
            }

            var series = new List<ISeries>();
            var limits = new List<double>();
            if (usl.HasValue) { series.Add(AxisSpecLine("USL", usl.Value, values.Count, AudioChartPalette.SpecLimit)); limits.Add(usl.Value); }
            if (nominal.HasValue) { series.Add(AxisSpecLine("Nominal", nominal.Value, values.Count, AudioChartPalette.SpecNominal)); limits.Add(nominal.Value); }
            if (lsl.HasValue) { series.Add(AxisSpecLine("LSL", lsl.Value, values.Count, AudioChartPalette.SpecLimit)); limits.Add(lsl.Value); }

            series.Add(new LineSeries<ObservablePoint>
            {
                Name = "Point " + SelectedAxisPoint + " " + SelectedAxisName,
                Values = points,
                Stroke = AudioChartPalette.Line(AudioChartPalette.SeriesPrimary, 1.5f),
                Fill = null,
                GeometrySize = values.Count > 200 ? 0 : 5,
                GeometryStroke = AudioChartPalette.Line(AudioChartPalette.SeriesPrimary, 1.5f),
                GeometryFill = AudioChartPalette.Fill(AudioChartPalette.Stroke),
                LineSmoothness = 0,
                XToolTipLabelFormatter = point => "#" + point.Coordinate.SecondaryValue.ToString("0"),
                YToolTipLabelFormatter = point => point.Coordinate.PrimaryValue.ToString("0.000"),
            });

            series.Add(new LineSeries<ObservablePoint>
            {
                Name = "NG",
                Values = outOfSpec,
                Stroke = null,
                Fill = null,
                GeometrySize = 9,
                GeometryStroke = AudioChartPalette.Line(AudioChartPalette.Fail, 2),
                GeometryFill = AudioChartPalette.Fill(AudioChartPalette.Fail),
                IsHoverable = false,
            });

            double low = Math.Min(values.Min(), limits.Count > 0 ? limits.Min() : values.Min());
            double high = Math.Max(values.Max(), limits.Count > 0 ? limits.Max() : values.Max());
            double pad = Math.Max(0.005, (high - low) * 0.1);

            AxisSeries = series.ToArray();
            AxisXAxes = new[]
            {
                new Axis
                {
                    MinLimit = 0,
                    MaxLimit = values.Count + 1,
                    Labeler = value => value.ToString("0", CultureInfo.InvariantCulture),
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 11,
                    SeparatorsPaint = null,
                },
            };
            AxisYAxes = new[]
            {
                new Axis
                {
                    MinLimit = low - pad,
                    MaxLimit = high + pad,
                    Labeler = value => value.ToString("0.000", CultureInfo.InvariantCulture),
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 11,
                    SeparatorsPaint = AudioChartPalette.Line(AudioChartPalette.GridLine, 1),
                },
                new Axis
                {
                    Position = AxisPosition.End,
                    MinLimit = low - pad,
                    MaxLimit = high + pad,
                    CustomSeparators = limits.ToArray(),
                    Labeler = value => SpecLabel(value, lsl ?? double.NaN, nominal ?? double.NaN, usl ?? double.NaN),
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.SpecLimit),
                    TextSize = 11,
                    SeparatorsPaint = null,
                },
            };
        }

        private static LineSeries<ObservablePoint> AxisSpecLine(string name, double value, int count, SkiaSharp.SKColor colour)
        {
            return new LineSeries<ObservablePoint>
            {
                Name = name,
                Values = new[] { new ObservablePoint(0, value), new ObservablePoint(count + 1, value) },
                Stroke = DashedLine(colour),
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0,
                ScalesYAt = 1,
                IsHoverable = false,
            };
        }

        private IReadOnlyList<string> AxisExportHeaders()
        {
            return new[] { "Time", "Unit SN", "Point", "Axis", "Value", "LSL", "Nominal", "USL", "Result" };
        }

        /// <summary>Export writes every point and axis in the window, not only the one on screen.</summary>
        private IReadOnlyList<IReadOnlyList<string>> AxisExportRows()
        {
            return _axisRecords.Select(r => (IReadOnlyList<string>)new[]
            {
                r.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                r.UnitSn,
                r.Point,
                r.Axis,
                r.Value.ToString("0.000", CultureInfo.InvariantCulture),
                r.Lsl.HasValue ? r.Lsl.Value.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty,
                r.Nominal.HasValue ? r.Nominal.Value.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty,
                r.Usl.HasValue ? r.Usl.Value.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty,
                new CoordinateRow(r).Result,
            }).ToList();
        }
    }
}
