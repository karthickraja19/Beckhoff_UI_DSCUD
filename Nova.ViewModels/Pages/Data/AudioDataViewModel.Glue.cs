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
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using Nova.Abstractions.Data.Models;
using Nova.ViewModels.Audio;
using SkiaSharp;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The Glue tab - S2.4.4.5 Glue Weight (slides 2 to 4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Layout the slides': the glue weight SPC trend chart across the top, one chart
    /// per gantry, each with its own glue path buttons; below, on the left the glue
    /// weight / speed regression diagram (its own gantry and path buttons) over the
    /// regression function, and on the right the raw data over the weighing settings.
    /// </para>
    /// <para>
    /// The trend follows the summary column's Hourly | Daily switch, as slides 3 and 4
    /// show: one point per hour of the latest 12, or per day of the latest seven, each
    /// the mean of the weighings in it.
    /// </para>
    /// </remarks>
    public partial class AudioDataViewModel
    {
        private const string FrontGantry = "Front";
        private const string RearGantry = "Rear";

        private IReadOnlyList<GlueWeighingRecord> _glueRecords = Empty<GlueWeighingRecord>();
        private IReadOnlyList<GlueRegression> _regressions = Empty<GlueRegression>();
        private GlueWeighingSettings _weighingSettings;


        // ==================================================================
        // SPC trend charts, one per gantry, each with its own path buttons
        // ==================================================================

        /// <summary>"Path 1", "Path 2", ... as found in the weighings and calibrations.</summary>
        public ObservableCollection<string> GluePaths { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        private string _frontGluePath;

        [ObservableProperty]
        private string _rearGluePath;

        partial void OnFrontGluePathChanged(string value)
        {
            BuildGlueTrend(FrontGantry);
        }

        partial void OnRearGluePathChanged(string value)
        {
            BuildGlueTrend(RearGantry);
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
        private Axis[] _frontGlueYAxes = new Axis[0];

        [ObservableProperty]
        private Axis[] _rearGlueYAxes = new Axis[0];

        /// <summary>
        /// The date(s) the hourly trend covers, shown beside each gantry's name. Slide 3
        /// prints the date under every hour, but a chart label here is one line, and
        /// twelve repeats of one date is clutter. Empty in Daily, whose labels are dates.
        /// </summary>
        [ObservableProperty]
        private string _glueTrendDateText = string.Empty;

        [ObservableProperty]
        private bool _hasFrontGlue;

        [ObservableProperty]
        private bool _hasRearGlue;

        /// <summary>The raw data table, newest first.</summary>
        public ObservableCollection<GlueWeighingRow> GlueWeighings { get; } = new ObservableCollection<GlueWeighingRow>();


        // ==================================================================
        // Regression diagram: its own gantry and path buttons
        // ==================================================================

        public ObservableCollection<string> RegressionGantries { get; } =
            new ObservableCollection<string> { FrontGantry, RearGantry };

        [ObservableProperty]
        private string _regressionGantry = FrontGantry;

        [ObservableProperty]
        private string _regressionPath;

        partial void OnRegressionGantryChanged(string value)
        {
            BuildRegression();
        }

        partial void OnRegressionPathChanged(string value)
        {
            BuildRegression();
        }

        [ObservableProperty]
        private ISeries[] _regressionSeries = new ISeries[0];

        [ObservableProperty]
        private Axis[] _regressionXAxes = new Axis[0];

        [ObservableProperty]
        private Axis[] _regressionYAxes = new Axis[0];

        /// <summary>"y = -0.0538x + 3.0521", printed on the diagram as slide 3 does.</summary>
        [ObservableProperty]
        private string _regressionEquation = string.Empty;

        /// <summary>"R² = 0.9552".</summary>
        [ObservableProperty]
        private string _regressionR2Text = string.Empty;

        [ObservableProperty]
        private string _regressionCalibratedText = string.Empty;

        [ObservableProperty]
        private bool _hasRegression;


        // ==================================================================
        // Glue weighing setting: S2.4.4.5's Item | Settings table
        // ==================================================================

        /// <summary>"2 hours/one time", or "Off".</summary>
        public string WeighingFrequencyText => _weighingSettings == null ? "—"
            : _weighingSettings.ByTime ? FormatMinutesPerTime(_weighingSettings.IntervalMinutes) : "Off";

        /// <summary>Shots per weighing - the slide's "Weighing times".</summary>
        public string WeighingTimesText => _weighingSettings == null ? "—"
            : _weighingSettings.ShotsPerWeighing.ToString(CultureInfo.InvariantCulture);

        /// <summary>"24 hours/one time".</summary>
        public string CalibrationFrequencyText => _weighingSettings == null ? "—"
            : FormatHoursPerTime(_weighingSettings.CalibrationIntervalHours);

        /// <summary>The SOP's extra triggers - shown only when one is on, so the table is the slide's three rows otherwise.</summary>
        public bool HasUnitTrigger => _weighingSettings != null && _weighingSettings.ByUnitCount;

        public string UnitTriggerText => _weighingSettings == null ? "—"
            : _weighingSettings.IntervalUnits.ToString(CultureInfo.InvariantCulture) + " units/one time";

        public bool HasShiftTrigger => _weighingSettings != null && (_weighingSettings.AtShiftStart || _weighingSettings.AtShiftEnd);

        public string ShiftTriggerText => _weighingSettings == null ? "—"
            : _weighingSettings.AtShiftStart && _weighingSettings.AtShiftEnd ? "Shift start and end"
            : _weighingSettings.AtShiftStart ? "Shift start"
            : _weighingSettings.AtShiftEnd ? "Shift end"
            : "Off";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(EditWeighingSettingsCommand))]
        private bool _hasWeighingSettings;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(EditWeighingSettingsCommand))]
        [NotifyPropertyChangedFor(nameof(ShowUnitTriggerRow), nameof(ShowShiftTriggerRow))]
        private bool _isEditingWeighing;

        /// <summary>The unit-count row: when that trigger is on, or while editing so it can be switched on.</summary>
        public bool ShowUnitTriggerRow => HasUnitTrigger || IsEditingWeighing;

        public bool ShowShiftTriggerRow => HasShiftTrigger || IsEditingWeighing;

        [ObservableProperty]
        private bool _weighByTime;

        /// <summary>Edited in hours, as the slide states it; kept in minutes.</summary>
        [ObservableProperty]
        private string _weighIntervalHours = string.Empty;

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
        private string _calibrationHours = string.Empty;

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

            double hours = 0;
            int units = 0, shots, calibration;

            if (WeighByTime && !TryParseHours(WeighIntervalHours, out hours))
                WeighingSettingsError = "Weighing frequency must be a number of hours, at least one minute.";
            else if (WeighByUnitCount && !TryParseCount(WeighIntervalUnits, out units))
                WeighingSettingsError = "Unit interval must be a whole number, 1 or more.";
            else if (!TryParseCount(WeighShots, out shots))
                WeighingSettingsError = "Weighing times must be a whole number, 1 or more.";
            else if (!TryParseCount(CalibrationHours, out calibration))
                WeighingSettingsError = "Calibration frequency must be a whole number of hours, 1 or more.";
            else if (!WeighByTime && !WeighByUnitCount && !WeighAtShiftStart && !WeighAtShiftEnd)
                WeighingSettingsError = "Choose at least one trigger, or the scale never weighs.";
            else
            {
                // Keep the old interval when a trigger is switched off, so switching it
                // back on does not come back as 0.
                var settings = new GlueWeighingSettings
                {
                    ByTime = WeighByTime,
                    IntervalMinutes = WeighByTime ? (int)Math.Round(hours * 60) : (_weighingSettings?.IntervalMinutes ?? 0),
                    ByUnitCount = WeighByUnitCount,
                    IntervalUnits = WeighByUnitCount ? units : (_weighingSettings?.IntervalUnits ?? 0),
                    AtShiftStart = WeighAtShiftStart,
                    AtShiftEnd = WeighAtShiftEnd,
                    ShotsPerWeighing = shots,
                    CalibrationIntervalHours = calibration
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

            if (s != null)
            {
                WeighByTime = s.ByTime;
                WeighIntervalHours = (s.IntervalMinutes / 60d).ToString("0.##", CultureInfo.InvariantCulture);
                WeighByUnitCount = s.ByUnitCount;
                WeighIntervalUnits = s.IntervalUnits.ToString(CultureInfo.InvariantCulture);
                WeighAtShiftStart = s.AtShiftStart;
                WeighAtShiftEnd = s.AtShiftEnd;
                WeighShots = s.ShotsPerWeighing.ToString(CultureInfo.InvariantCulture);
                CalibrationHours = s.CalibrationIntervalHours.ToString(CultureInfo.InvariantCulture);
            }

            OnPropertyChanged(nameof(WeighingFrequencyText));
            OnPropertyChanged(nameof(WeighingTimesText));
            OnPropertyChanged(nameof(CalibrationFrequencyText));
            OnPropertyChanged(nameof(HasUnitTrigger));
            OnPropertyChanged(nameof(UnitTriggerText));
            OnPropertyChanged(nameof(HasShiftTrigger));
            OnPropertyChanged(nameof(ShiftTriggerText));
            OnPropertyChanged(nameof(ShowUnitTriggerRow));
            OnPropertyChanged(nameof(ShowShiftTriggerRow));
        }

        private static string WeighingSummary(GlueWeighingSettings s)
        {
            var parts = new List<string>();
            if (s.ByTime) parts.Add("every " + s.IntervalMinutes + " min");
            if (s.ByUnitCount) parts.Add("every " + s.IntervalUnits + " units");
            if (s.AtShiftStart) parts.Add("shift start");
            if (s.AtShiftEnd) parts.Add("shift end");
            parts.Add(s.ShotsPerWeighing + " weighing times");
            parts.Add("calibrate every " + s.CalibrationIntervalHours + " h");
            return string.Join(", ", parts);
        }

        /// <summary>"2 hours/one time"; minutes when the interval is not whole hours.</summary>
        private static string FormatMinutesPerTime(int minutes)
        {
            return minutes > 0 && minutes % 60 == 0
                ? FormatHoursPerTime(minutes / 60)
                : minutes + (minutes == 1 ? " minute" : " minutes") + "/one time";
        }

        private static string FormatHoursPerTime(int hours)
        {
            return hours + (hours == 1 ? " hour" : " hours") + "/one time";
        }

        private static bool TryParseCount(string text, out int value)
        {
            return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                   && value >= 1;
        }

        private static bool TryParseHours(string text, out double hours)
        {
            return GantryDosageRow.TryParsePositive(text, out hours) && Math.Round(hours * 60) >= 1;
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
                GlueStatus = "The machine does not report glue weighings yet, so there are none to show. "
                             + "Each weighing is recorded once the PLC provides it.";
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
            List<string> paths = _glueRecords.Select(r => r.Path)
                .Concat(_regressions.Select(r => r.Path))
                .Where(p => p > 0)
                .Distinct()
                .OrderBy(p => p)
                .Select(p => "Path " + p)
                .ToList();

            string keepFront = FrontGluePath;
            string keepRear = RearGluePath;
            string keepRegression = RegressionPath;

            GluePaths.Clear();
            foreach (string path in paths)
                GluePaths.Add(path);

            // Each selection that does not change is rebuilt here; one that does
            // rebuilds itself through its changed handler.
            string front = paths.Contains(keepFront) ? keepFront : paths.FirstOrDefault();
            if (front == FrontGluePath)
                BuildGlueTrend(FrontGantry);
            else
                FrontGluePath = front;

            string rear = paths.Contains(keepRear) ? keepRear : paths.FirstOrDefault();
            if (rear == RearGluePath)
                BuildGlueTrend(RearGantry);
            else
                RearGluePath = rear;

            string regression = paths.Contains(keepRegression) ? keepRegression : paths.FirstOrDefault();
            if (regression == RegressionPath)
                BuildRegression();
            else
                RegressionPath = regression;

            GlueWeighings.Clear();
            foreach (GlueWeighingRecord record in _glueRecords.OrderByDescending(r => r.Time).Take(MaxTableRows))
                GlueWeighings.Add(new GlueWeighingRow(record));
        }

        private static int PathNumber(string pathText)
        {
            int path;
            return pathText != null &&
                   int.TryParse(pathText.Replace("Path", string.Empty).Trim(), out path)
                ? path
                : -1;
        }

        private List<GlueWeighingRecord> ForGantry(string gantry, int path)
        {
            return _glueRecords
                .Where(r => r.Path == path && string.Equals(r.Gantry, gantry, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Time)
                .ToList();
        }

        /// <summary>
        /// One gantry's trend for its chosen path: the mean weight of each hour (or day)
        /// as a point with its value printed over it, out-of-spec points red, and USL,
        /// Nominal and LSL as dashed lines named on the right, as slides 3 and 4 draw them.
        /// </summary>
        private void BuildGlueTrend(string gantry)
        {
            bool front = gantry == FrontGantry;
            List<GlueWeighingRecord> records = ForGantry(gantry, PathNumber(front ? FrontGluePath : RearGluePath));
            List<DateTime> buckets = BucketStarts();

            ISeries[] series = new ISeries[0];
            Axis[] xAxes = GlueTrendXAxes(buckets);
            Axis[] yAxes = new Axis[0];

            if (records.Count > 0 && buckets.Count > 0)
            {
                var index = new Dictionary<DateTime, int>();
                for (int i = 0; i < buckets.Count; i++)
                    index[buckets[i]] = i;

                // Points only where there were weighings, joined across the empty
                // buckets between them - the scale weighs every couple of hours, and a
                // line broken at every empty hour would read as missing data.
                var points = new List<ObservablePoint>();
                var outOfSpec = new List<ObservablePoint>();
                foreach (var bucket in records.GroupBy(r => GlueBucketOf(r.Time)).OrderBy(g => g.Key))
                {
                    int i;
                    if (!index.TryGetValue(bucket.Key, out i))
                        continue;

                    double mean = bucket.Average(r => r.WeightMg);
                    GlueWeighingRecord last = bucket.Last();
                    points.Add(new ObservablePoint(i, mean));
                    if (mean < last.LowerLimitMg || mean > last.UpperLimitMg)
                        outOfSpec.Add(new ObservablePoint(i, mean));
                }

                // The spec lines are the latest weighing's limits.
                GlueWeighingRecord latest = records[records.Count - 1];
                double lsl = latest.LowerLimitMg, nominal = latest.NominalMg, usl = latest.UpperLimitMg;

                double low = Math.Min(lsl, points.Count > 0 ? points.Min(p => p.Y ?? lsl) : lsl);
                double high = Math.Max(usl, points.Count > 0 ? points.Max(p => p.Y ?? usl) : usl);
                double pad = Math.Max(0.1, (high - low) * 0.15);
                double min = Math.Floor((low - pad) * 2) / 2;
                double max = Math.Ceiling((high + pad) * 2) / 2;

                series = new ISeries[]
                {
                    SpecLine("USL", usl, buckets.Count, AudioChartPalette.SpecLimit),
                    SpecLine("Nominal", nominal, buckets.Count, AudioChartPalette.SpecNominal),
                    SpecLine("LSL", lsl, buckets.Count, AudioChartPalette.SpecLimit),
                    new LineSeries<ObservablePoint>
                    {
                        Name = "Gantry: " + gantry,
                        Values = points,
                        Stroke = AudioChartPalette.Line(AudioChartPalette.SeriesPrimary, 2),
                        Fill = null,
                        GeometrySize = 7,
                        GeometryStroke = AudioChartPalette.Line(AudioChartPalette.SeriesPrimary, 2),
                        GeometryFill = AudioChartPalette.Fill(AudioChartPalette.Stroke),
                        LineSmoothness = 0.5,
                        DataLabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                        DataLabelsSize = 11,
                        DataLabelsPosition = DataLabelsPosition.Top,
                        DataLabelsFormatter = point => point.Coordinate.PrimaryValue.ToString("0.000"),
                        XToolTipLabelFormatter = point => GlueBucketLabel(buckets, (int)point.Coordinate.SecondaryValue),
                        YToolTipLabelFormatter = point => point.Coordinate.PrimaryValue.ToString("0.000") + " mg"
                    },
                    new LineSeries<ObservablePoint>
                    {
                        Name = "Out of spec",
                        Values = outOfSpec,
                        Stroke = null,
                        Fill = null,
                        GeometrySize = 11,
                        GeometryStroke = AudioChartPalette.Line(AudioChartPalette.Fail, 2),
                        GeometryFill = AudioChartPalette.Fill(AudioChartPalette.Fail),
                        IsHoverable = false
                    }
                };

                yAxes = new[]
                {
                    new Axis
                    {
                        MinLimit = min,
                        MaxLimit = max,
                        Labeler = value => value.ToString("0.0#"),
                        LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                        TextSize = 11,
                        SeparatorsPaint = AudioChartPalette.Line(AudioChartPalette.GridLine, 1)
                    },
                    // The right-hand axis names the spec lines where they cross it.
                    new Axis
                    {
                        Position = AxisPosition.End,
                        MinLimit = min,
                        MaxLimit = max,
                        CustomSeparators = new[] { lsl, nominal, usl },
                        Labeler = value => SpecLabel(value, lsl, nominal, usl),
                        LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.SpecLimit),
                        TextSize = 11,
                        SeparatorsPaint = null
                    }
                };
            }

            if (front)
            {
                HasFrontGlue = series.Length > 0;
                FrontGlueSeries = series;
                FrontGlueXAxes = xAxes;
                FrontGlueYAxes = yAxes;
            }
            else
            {
                HasRearGlue = series.Length > 0;
                RearGlueSeries = series;
                RearGlueXAxes = xAxes;
                RearGlueYAxes = yAxes;
            }
        }

        /// <summary>A dashed spec line across the whole chart, on the right-hand axis.</summary>
        private static LineSeries<ObservablePoint> SpecLine(string name, double value, int buckets, SKColor colour)
        {
            return new LineSeries<ObservablePoint>
            {
                Name = name,
                Values = new[] { new ObservablePoint(-0.5, value), new ObservablePoint(buckets - 0.5, value) },
                Stroke = DashedLine(colour),
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0,
                ScalesYAt = 1,
                IsHoverable = false
            };
        }

        private static string SpecLabel(double value, double lsl, double nominal, double usl)
        {
            const double tolerance = 1e-6;
            if (Math.Abs(value - usl) < tolerance) return "USL " + usl.ToString("0.0##");
            if (Math.Abs(value - nominal) < tolerance) return "Nominal " + nominal.ToString("0.0##");
            if (Math.Abs(value - lsl) < tolerance) return "LSL " + lsl.ToString("0.0##");
            return string.Empty;
        }

        /// <summary>
        /// One label per bucket: "08:00-09:00" hourly, "2024/11/18" daily. Hourly, the
        /// date goes beside the gantry's name (<see cref="GlueTrendDateText"/>) rather
        /// than in an axis title, which would take chart height the tab cannot spare.
        /// </summary>
        private Axis[] GlueTrendXAxes(List<DateTime> buckets)
        {
            var labels = buckets.Select((b, i) => GlueBucketLabel(buckets, i)).ToList();

            string dates = string.Empty;
            if (!IsDaily && buckets.Count > 0)
            {
                string first = buckets[0].ToString("yyyy/M/d");
                string last = buckets[buckets.Count - 1].ToString("yyyy/M/d");
                dates = first == last ? first : first + " - " + last;
            }

            GlueTrendDateText = dates;

            return new[]
            {
                new Axis
                {
                    MinLimit = -0.5,
                    MaxLimit = Math.Max(0, buckets.Count - 1) + 0.5,
                    CustomSeparators = Enumerable.Range(0, buckets.Count).Select(i => (double)i).ToArray(),
                    Labeler = value =>
                    {
                        int i = (int)Math.Round(value);
                        return i >= 0 && i < labels.Count ? labels[i] : string.Empty;
                    },
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 10,
                    LabelsRotation = buckets.Count > 12 ? 45 : 0,
                    SeparatorsPaint = null
                }
            };
        }

        private string GlueBucketLabel(List<DateTime> buckets, int i)
        {
            if (i < 0 || i >= buckets.Count)
                return string.Empty;

            DateTime b = buckets[i];
            return IsDaily ? b.ToString("yyyy/M/d") : b.ToString("HH:00") + "-" + b.AddHours(1).ToString("HH:00");
        }

        /// <summary>A weighing's hour, or its calendar day in Daily.</summary>
        private DateTime GlueBucketOf(DateTime time)
        {
            return IsDaily ? time.Date : new DateTime(time.Year, time.Month, time.Day, time.Hour, 0, 0);
        }

        /// <summary>
        /// The chosen gantry and path's last calibration: its ten points in red, the
        /// fitted line, and the equation and R² printed on the diagram (slide 3).
        /// </summary>
        private void BuildRegression()
        {
            int path = PathNumber(RegressionPath);
            GlueRegression regression = _regressions
                .Where(r => string.Equals(r.Gantry, RegressionGantry, StringComparison.OrdinalIgnoreCase) && r.Path == path)
                .OrderByDescending(r => r.CalibratedAt ?? DateTime.MinValue)
                .FirstOrDefault();

            IReadOnlyList<GlueCalibrationPoint> points = regression?.Points ?? new GlueCalibrationPoint[0];
            HasRegression = regression != null && points.Count > 0;

            if (!HasRegression)
            {
                RegressionSeries = new ISeries[0];
                RegressionEquation = RegressionR2Text = RegressionCalibratedText = string.Empty;
                BuildRegressionAxes(regression);
                return;
            }

            double minX = points.Min(p => p.X);
            double maxX = points.Max(p => p.X);

            RegressionSeries = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Name = "Regression",
                    Values = new[]
                    {
                        new ObservablePoint(minX, regression.W * minX + regression.B),
                        new ObservablePoint(maxX, regression.W * maxX + regression.B)
                    },
                    Stroke = AudioChartPalette.Line(AudioChartPalette.SeriesPrimary, 1.5f),
                    Fill = null,
                    GeometrySize = 0,
                    LineSmoothness = 0,
                    IsHoverable = false
                },
                new ScatterSeries<ObservablePoint>
                {
                    Name = "Calibration points",
                    Values = points.Select(p => new ObservablePoint(p.X, p.Y)).ToArray(),
                    GeometrySize = 7,
                    Fill = AudioChartPalette.Fill(AudioChartPalette.Fail),
                    Stroke = null,
                    XToolTipLabelFormatter = point => point.Coordinate.SecondaryValue.ToString("0.##") + " mm/s",
                    YToolTipLabelFormatter = point => point.Coordinate.PrimaryValue.ToString("0.000") + " mg"
                }
            };

            RegressionEquation = "y = " + regression.W.ToString("0.0000") + "x "
                                 + (regression.B < 0 ? "- " : "+ ") + Math.Abs(regression.B).ToString("0.0000");
            RegressionR2Text = double.IsNaN(regression.R2) ? "R² not reported" : "R² = " + regression.R2.ToString("0.0000");
            RegressionCalibratedText = regression.CalibratedAt.HasValue
                ? "Calibrated " + regression.CalibratedAt.Value.ToString("yyyy/M/d HH:mm")
                : string.Empty;

            BuildRegressionAxes(regression);
        }

        /// <remarks>
        /// The weight axis leaves the top third of the diagram clear, so the equation
        /// printed there (slide 3) never sits on a calibration point; the speed axis
        /// starts at 0, as the slide's does.
        /// </remarks>
        private void BuildRegressionAxes(GlueRegression regression)
        {
            string input = regression != null && !string.IsNullOrWhiteSpace(regression.InputName)
                ? regression.InputName
                : "Dispensing speed (mm/s)";

            double? minX = null, maxX = null, minY = null, maxY = null;
            IReadOnlyList<GlueCalibrationPoint> points = regression?.Points;
            if (points != null && points.Count > 0)
            {
                var ys = points.Select(p => p.Y)
                    .Concat(points.Select(p => regression.W * p.X + regression.B))
                    .ToList();
                double low = ys.Min(), high = ys.Max();
                double range = Math.Max(high - low, 0.05);

                minX = 0;
                maxX = Math.Ceiling(points.Max(p => p.X)) + 1;
                minY = low - range * 0.25;
                maxY = high + range * 0.9;
            }

            RegressionXAxes = new[]
            {
                new Axis
                {
                    MinLimit = minX,
                    MaxLimit = maxX,
                    Name = input,
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
                    MinLimit = minY,
                    MaxLimit = maxY,
                    Name = "Glue weight (mg)",
                    NameTextSize = 11,
                    NamePaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    Labeler = value => value.ToString("0.0#"),
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 10,
                    SeparatorsPaint = AudioChartPalette.Line(AudioChartPalette.GridLine, 1)
                }
            };
        }


        // ==================================================================
        // Export
        // ==================================================================

        private static IReadOnlyList<string> GlueExportHeaders()
        {
            return new[] { "Date", "Time", "Shift", "Gantry", "Path", "Speed (mm/s)", "Value (mg)", "Nominal (mg)", "LSL (mg)", "USL (mg)", "Result" };
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
                    r.DispenseSpeed.ToString("0.##"),
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
