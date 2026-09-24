using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using Nova.Abstractions.Data.Models;
using Nova.ViewModels.Audio;
using SkiaSharp;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The Glue tab - S2.4.4.5 Glue Weight: a trend chart per gantry for the chosen glue
    /// path with its spec lines, the raw weighings, the regression from the last
    /// calibration, and the weighing settings.
    /// </summary>
    public partial class AudioDataViewModel
    {
        private const string FrontGantry = "Front";
        private const string RearGantry = "Rear";

        private IReadOnlyList<GlueWeighingRecord> _glueRecords = Empty<GlueWeighingRecord>();
        private IReadOnlyList<GlueRegression> _regressions = Empty<GlueRegression>();
        private GlueWeighingSettings _weighingSettings;


        // ==================================================================
        // Trend charts, one per gantry (S2.4.4.5 page 3)
        // ==================================================================

        /// <summary>"Path 1", "Path 2", ... as found in the weighings.</summary>
        public ObservableCollection<string> GluePaths { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        private string _selectedGluePath;

        partial void OnSelectedGluePathChanged(string value)
        {
            BuildGlueTrends();
        }

        [ObservableProperty]
        private ISeries[] _frontGlueSeries = new ISeries[0];

        [ObservableProperty]
        private ISeries[] _rearGlueSeries = new ISeries[0];

        [ObservableProperty]
        private Axis[] _frontGlueXAxes = new Axis[0];

        [ObservableProperty]
        private Axis[] _rearGlueXAxes = new Axis[0];

        [ObservableProperty]
        private Axis[] _glueYAxes = new Axis[0];

        /// <summary>"n 24 · mean 3.21 · σ 0.18 · Cpk 1.85" for the front gantry's chosen path.</summary>
        [ObservableProperty]
        private string _frontGlueStats = string.Empty;

        [ObservableProperty]
        private string _rearGlueStats = string.Empty;

        [ObservableProperty]
        private bool _hasFrontGlue;

        [ObservableProperty]
        private bool _hasRearGlue;

        /// <summary>The raw data table, newest first.</summary>
        public ObservableCollection<GlueWeighingRow> GlueWeighings { get; } = new ObservableCollection<GlueWeighingRow>();


        // ==================================================================
        // Regression (HIVE w, b, R2)
        // ==================================================================

        public ObservableCollection<GlueRegressionRow> GlueRegressions { get; } = new ObservableCollection<GlueRegressionRow>();

        [ObservableProperty]
        private ISeries[] _regressionSeries = new ISeries[0];

        [ObservableProperty]
        private Axis[] _regressionXAxes = new Axis[0];

        [ObservableProperty]
        private Axis[] _regressionYAxes = new Axis[0];

        public bool HasRegressions => GlueRegressions.Count > 0;


        // ==================================================================
        // Weighing settings (SOP: time, count, shift start/end; S2.4.4.5)
        // ==================================================================

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(EditWeighingSettingsCommand))]
        private bool _hasWeighingSettings;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(EditWeighingSettingsCommand))]
        private bool _isEditingWeighing;

        [ObservableProperty]
        private bool _weighByTime;

        [ObservableProperty]
        private string _weighIntervalMinutes = string.Empty;

        [ObservableProperty]
        private bool _weighByUnitCount;

        [ObservableProperty]
        private string _weighIntervalUnits = string.Empty;

        [ObservableProperty]
        private bool _weighAtShiftStart;

        [ObservableProperty]
        private bool _weighAtShiftEnd;

        [ObservableProperty]
        private string _weighShots = string.Empty;

        [ObservableProperty]
        private string _calibrationDays = string.Empty;

        [ObservableProperty]
        private string _weighingSettingsError;

        /// <summary>Level 2 and up, once the settings have been read (built by hand: the toolkit's CanExecute needs C# 9).</summary>
        public IRelayCommand EditWeighingSettingsCommand { get; private set; }

        private bool CanEditWeighing()
        {
            return CanEditSettings && HasWeighingSettings && !IsEditingWeighing;
        }

        private void EditWeighingSettings()
        {
            WeighingSettingsError = null;
            IsEditingWeighing = true;
        }

        [RelayCommand]
        private void CancelWeighingSettings()
        {
            WeighingSettingsError = null;
            IsEditingWeighing = false;
            LoadWeighingFields();
        }

        [RelayCommand]
        private async Task SaveWeighingSettingsAsync()
        {
            if (_dispenser == null || !CanEditSettings)
                return;

            int minutes = 0, units = 0, shots, days;

            if (WeighByTime && !TryParseCount(WeighIntervalMinutes, out minutes))
                WeighingSettingsError = "Time interval must be a whole number of minutes, 1 or more.";
            else if (WeighByUnitCount && !TryParseCount(WeighIntervalUnits, out units))
                WeighingSettingsError = "Unit interval must be a whole number, 1 or more.";
            else if (!TryParseCount(WeighShots, out shots))
                WeighingSettingsError = "Shots per weighing must be a whole number, 1 or more.";
            else if (!TryParseCount(CalibrationDays, out days))
                WeighingSettingsError = "Calibration interval must be a whole number of days, 1 or more.";
            else if (!WeighByTime && !WeighByUnitCount && !WeighAtShiftStart && !WeighAtShiftEnd)
                WeighingSettingsError = "Choose at least one trigger, or the scale never weighs.";
            else
            {
                // Keep the old interval when a trigger is switched off, so switching it
                // back on does not come back as 0.
                var settings = new GlueWeighingSettings
                {
                    ByTime = WeighByTime,
                    IntervalMinutes = WeighByTime ? minutes : (_weighingSettings?.IntervalMinutes ?? 0),
                    ByUnitCount = WeighByUnitCount,
                    IntervalUnits = WeighByUnitCount ? units : (_weighingSettings?.IntervalUnits ?? 0),
                    AtShiftStart = WeighAtShiftStart,
                    AtShiftEnd = WeighAtShiftEnd,
                    ShotsPerWeighing = shots,
                    CalibrationIntervalDays = days
                };

                try
                {
                    await _dispenser.SaveWeighingSettingsAsync(settings);
                    _weighingSettings = settings;
                    WeighingSettingsError = null;
                    IsEditingWeighing = false;
                    LoadWeighingFields();
                    LogAction("Glue weighing settings changed: " + WeighingSummary(settings));
                }
                catch (Exception ex)
                {
                    WeighingSettingsError = "Could not save: " + ex.Message;
                }
            }
        }

        private void LoadWeighingFields()
        {
            GlueWeighingSettings s = _weighingSettings;
            HasWeighingSettings = s != null;

            if (s == null)
                return;

            WeighByTime = s.ByTime;
            WeighIntervalMinutes = s.IntervalMinutes.ToString(CultureInfo.InvariantCulture);
            WeighByUnitCount = s.ByUnitCount;
            WeighIntervalUnits = s.IntervalUnits.ToString(CultureInfo.InvariantCulture);
            WeighAtShiftStart = s.AtShiftStart;
            WeighAtShiftEnd = s.AtShiftEnd;
            WeighShots = s.ShotsPerWeighing.ToString(CultureInfo.InvariantCulture);
            CalibrationDays = s.CalibrationIntervalDays.ToString(CultureInfo.InvariantCulture);
        }

        private static string WeighingSummary(GlueWeighingSettings s)
        {
            var parts = new List<string>();
            if (s.ByTime) parts.Add("every " + s.IntervalMinutes + " min");
            if (s.ByUnitCount) parts.Add("every " + s.IntervalUnits + " units");
            if (s.AtShiftStart) parts.Add("shift start");
            if (s.AtShiftEnd) parts.Add("shift end");
            parts.Add(s.ShotsPerWeighing + " shots");
            parts.Add("calibrate every " + s.CalibrationIntervalDays + " days");
            return string.Join(", ", parts);
        }

        private static bool TryParseCount(string text, out int value)
        {
            return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                   && value >= 1;
        }


        // ==================================================================
        // Loading and building
        // ==================================================================

        private async Task LoadGlueAsync()
        {
            if (_dispenser == null)
            {
                _glueRecords = Empty<GlueWeighingRecord>();
                _regressions = Empty<GlueRegression>();
                _weighingSettings = null;
                GlueStatus = "The weighing scale is not connected to this HMI yet, so there are no glue weighings to show.";
                BuildGlue();
                LoadWeighingFields();
                return;
            }

            try
            {
                _glueRecords = await _dispenser.GetGlueWeighingsAsync(StartTime, EndTime) ?? Empty<GlueWeighingRecord>();
                _regressions = await _dispenser.GetGlueRegressionsAsync() ?? Empty<GlueRegression>();
                _weighingSettings = await _dispenser.GetWeighingSettingsAsync();

                int failed = _glueRecords.Count(r => !r.IsPass);
                GlueStatus = _glueRecords.Count == 0
                    ? "No glue weighings from " + StartTime.ToString("dd/MM/yyyy HH:mm") + " to " + EndTime.ToString("dd/MM/yyyy HH:mm") + "."
                    : _glueRecords.Count.ToString("N0") + " weighings, " + failed.ToString("N0") + " out of spec.";
            }
            catch (Exception ex)
            {
                _glueRecords = Empty<GlueWeighingRecord>();
                _regressions = Empty<GlueRegression>();
                GlueStatus = "Query failed: " + ex.Message;
            }

            BuildGlue();

            // An edit in progress keeps what the operator has typed.
            if (!IsEditingWeighing)
                LoadWeighingFields();

            ExportCommand.NotifyCanExecuteChanged();
        }

        private void BuildGlue()
        {
            List<string> paths = _glueRecords
                .Select(r => r.Path)
                .Distinct()
                .OrderBy(p => p)
                .Select(p => "Path " + p)
                .ToList();

            string keep = SelectedGluePath;
            GluePaths.Clear();
            foreach (string path in paths)
                GluePaths.Add(path);

            // Setting the selection rebuilds the trends through OnSelectedGluePathChanged;
            // when it does not change, rebuild them here.
            string select = paths.Contains(keep) ? keep : paths.FirstOrDefault();
            if (select == SelectedGluePath)
                BuildGlueTrends();
            else
                SelectedGluePath = select;

            GlueWeighings.Clear();
            foreach (GlueWeighingRecord record in _glueRecords.OrderByDescending(r => r.Time).Take(MaxTableRows))
                GlueWeighings.Add(new GlueWeighingRow(record));

            BuildRegression();
        }

        private int SelectedPathNumber()
        {
            int path;
            return SelectedGluePath != null &&
                   int.TryParse(SelectedGluePath.Replace("Path", string.Empty).Trim(), out path)
                ? path
                : -1;
        }

        private void BuildGlueTrends()
        {
            int path = SelectedPathNumber();

            List<GlueWeighingRecord> front = ForGantry(FrontGantry, path);
            List<GlueWeighingRecord> rear = ForGantry(RearGantry, path);

            HasFrontGlue = front.Count > 0;
            HasRearGlue = rear.Count > 0;

            FrontGlueSeries = TrendSeries(front);
            RearGlueSeries = TrendSeries(rear);
            FrontGlueXAxes = TrendXAxes(front);
            RearGlueXAxes = TrendXAxes(rear);
            FrontGlueStats = Stats(front);
            RearGlueStats = Stats(rear);

            GlueYAxes = new[]
            {
                new Axis
                {
                    Labeler = value => value.ToString("0.00"),
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 11,
                    SeparatorsPaint = AudioChartPalette.Line(AudioChartPalette.GridLine, 1)
                }
            };
        }

        private List<GlueWeighingRecord> ForGantry(string gantry, int path)
        {
            return _glueRecords
                .Where(r => r.Path == path && string.Equals(r.Gantry, gantry, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Time)
                .ToList();
        }

        /// <summary>
        /// The weighings as a line, the out-of-spec ones marked red on top, and USL,
        /// nominal and LSL as dashed lines. The spec lines follow each weighing's own
        /// limits, so a limit change part-way through the window shows as a step.
        /// </summary>
        private static ISeries[] TrendSeries(List<GlueWeighingRecord> records)
        {
            if (records.Count == 0)
                return new ISeries[0];

            return new ISeries[]
            {
                SpecLine("USL", records.Select(r => r.UpperLimitMg), AudioChartPalette.Downtime),
                SpecLine("Nominal", records.Select(r => r.NominalMg), AudioChartPalette.SeriesSecondary),
                SpecLine("LSL", records.Select(r => r.LowerLimitMg), AudioChartPalette.Downtime),
                new LineSeries<double>
                {
                    Name = "Weight (mg)",
                    Values = records.Select(r => r.WeightMg).ToArray(),
                    Stroke = AudioChartPalette.Line(AudioChartPalette.SeriesPrimary, 2),
                    Fill = null,
                    GeometrySize = 6,
                    GeometryStroke = AudioChartPalette.Line(AudioChartPalette.SeriesPrimary, 2),
                    GeometryFill = AudioChartPalette.Fill(AudioChartPalette.Stroke),
                    LineSmoothness = 0
                },
                new LineSeries<double?>
                {
                    Name = "Out of spec",
                    Values = records.Select(r => r.IsPass ? (double?)null : r.WeightMg).ToArray(),
                    Stroke = null,
                    Fill = null,
                    GeometrySize = 10,
                    GeometryStroke = AudioChartPalette.Line(AudioChartPalette.Downtime, 2),
                    GeometryFill = AudioChartPalette.Fill(AudioChartPalette.Downtime)
                }
            };
        }

        private static LineSeries<double> SpecLine(string name, IEnumerable<double> values, SKColor colour)
        {
            return new LineSeries<double>
            {
                Name = name,
                Values = values.ToArray(),
                Stroke = DashedLine(colour),
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0
            };
        }

        private static Axis[] TrendXAxes(List<GlueWeighingRecord> records)
        {
            var labels = records.Select(r => r.Time.ToString("MM/dd HH:mm")).ToList();
            return new[]
            {
                new Axis
                {
                    Labels = labels,
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 10,
                    LabelsRotation = labels.Count > 8 ? 45 : 0,
                    SeparatorsPaint = null
                }
            };
        }

        /// <summary>
        /// Count, mean, standard deviation and Cpk against the latest limits - the
        /// numbers an SPC chart is read for.
        /// </summary>
        private static string Stats(List<GlueWeighingRecord> records)
        {
            if (records.Count == 0)
                return "No weighings";

            double mean = records.Average(r => r.WeightMg);
            string text = "n " + records.Count + " · mean " + mean.ToString("0.000") + " mg";

            if (records.Count < 2)
                return text;

            double sigma = Math.Sqrt(records.Sum(r => (r.WeightMg - mean) * (r.WeightMg - mean)) / (records.Count - 1));
            text += " · σ " + sigma.ToString("0.000");

            GlueWeighingRecord latest = records[records.Count - 1];
            if (sigma > 0)
            {
                double cpk = Math.Min(latest.UpperLimitMg - mean, mean - latest.LowerLimitMg) / (3 * sigma);
                text += " · Cpk " + cpk.ToString("0.00");
            }

            int failed = records.Count(r => !r.IsPass);
            if (failed > 0)
                text += " · " + failed + " out of spec";

            return text;
        }

        /// <summary>
        /// The calibration points of each gantry as dots, and its fitted line
        /// y = w x + b drawn across them.
        /// </summary>
        private void BuildRegression()
        {
            GlueRegressions.Clear();
            var series = new List<ISeries>();
            string inputName = "Input";

            int i = 0;
            foreach (GlueRegression regression in _regressions
                         .OrderBy(r => string.Equals(r.Gantry, FrontGantry, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            {
                GlueRegressions.Add(new GlueRegressionRow(regression));

                if (!string.IsNullOrWhiteSpace(regression.InputName))
                    inputName = regression.InputName;

                SKColor colour = i == 0 ? AudioChartPalette.SeriesPrimary : AudioChartPalette.SeriesSecondary;
                i++;

                IReadOnlyList<GlueCalibrationPoint> points = regression.Points ?? new GlueCalibrationPoint[0];
                if (points.Count == 0)
                    continue;

                series.Add(new ScatterSeries<ObservablePoint>
                {
                    Name = regression.Gantry + " points",
                    Values = points.Select(p => new ObservablePoint(p.X, p.Y)).ToArray(),
                    GeometrySize = 9,
                    Fill = AudioChartPalette.Fill(colour),
                    Stroke = null
                });

                double minX = points.Min(p => p.X);
                double maxX = points.Max(p => p.X);
                series.Add(new LineSeries<ObservablePoint>
                {
                    Name = regression.Gantry + " fit",
                    Values = new[]
                    {
                        new ObservablePoint(minX, regression.W * minX + regression.B),
                        new ObservablePoint(maxX, regression.W * maxX + regression.B)
                    },
                    Stroke = AudioChartPalette.Line(colour, 1.5f),
                    Fill = null,
                    GeometrySize = 0,
                    LineSmoothness = 0
                });
            }

            RegressionSeries = series.ToArray();

            RegressionXAxes = new[]
            {
                new Axis
                {
                    Name = inputName,
                    NameTextSize = 11,
                    NamePaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 10,
                    SeparatorsPaint = AudioChartPalette.Line(AudioChartPalette.GridLine, 1)
                }
            };

            RegressionYAxes = new[]
            {
                new Axis
                {
                    Name = "Weight (mg)",
                    NameTextSize = 11,
                    NamePaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    Labeler = value => value.ToString("0.00"),
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 10,
                    SeparatorsPaint = AudioChartPalette.Line(AudioChartPalette.GridLine, 1)
                }
            };

            OnPropertyChanged(nameof(HasRegressions));
        }


        // ==================================================================
        // Export
        // ==================================================================

        private static IReadOnlyList<string> GlueExportHeaders()
        {
            return new[] { "Date", "Time", "Shift", "Gantry", "Path", "Weight (mg)", "Nominal (mg)", "LSL (mg)", "USL (mg)", "Result" };
        }

        private IReadOnlyList<IReadOnlyList<string>> GlueExportRows()
        {
            return _glueRecords
                .OrderByDescending(r => r.Time)
                .Select(r => (IReadOnlyList<string>)new[]
                {
                    r.Time.ToString("yyyy/MM/dd"),
                    r.Time.ToString("HH:mm:ss"),
                    r.Shift ?? string.Empty,
                    r.Gantry ?? string.Empty,
                    r.Path.ToString(),
                    r.WeightMg.ToString("0.000"),
                    r.NominalMg.ToString("0.000"),
                    r.LowerLimitMg.ToString("0.000"),
                    r.UpperLimitMg.ToString("0.000"),
                    r.IsPass ? "Pass" : "Fail"
                })
                .ToList();
        }
    }
}
