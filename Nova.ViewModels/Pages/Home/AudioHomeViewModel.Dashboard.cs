using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Data.Repositories;
using Nova.Abstractions.Services;
using Nova.ViewModels.Audio;
using SkiaSharp;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The four dashboard panels of slide 43: State Changes, Error Statistics, I/O
    /// Summary and the Parameter Dashboard.
    /// </summary>
    public partial class AudioHomeViewModel
    {
        private const string NoValue = "—";


        // ==================================================================
        // State Changes  (slide 43: "State Change dashboard shows past 7 days' state
        // data", "T = current date")
        // ==================================================================

        [ObservableProperty]
        private ISeries[] _stateChangeSeries = new ISeries[0];

        [ObservableProperty]
        private Axis[] _stateChangeXAxes = new Axis[0];

        [ObservableProperty]
        private Axis[] _stateChangeYAxes = new Axis[0];

        /// <summary>PLC code to DFM error type, as the Alarm page shows it; null for sample data.</summary>
        private readonly IErrorCodeCatalog _errorCodes;

        /// <summary>The DFM error type an alarm counts under (slide 43's Error Statistics).</summary>
        private string ErrorTypeOf(AlarmData alarm)
        {
            if (_errorCodes != null)
                return _errorCodes.Translate(alarm.AlarmCode, alarm.AlarmDescription).Message;

            return string.IsNullOrWhiteSpace(alarm.AlarmType) ? "Unclassified" : alarm.AlarmType.Trim();
        }

        /// <summary>Shown under the chart when there is nothing to draw, or no source.</summary>
        [ObservableProperty]
        private string _stateChangeNote = string.Empty;

        /// <summary>
        /// Seven stacked columns, T-6 to T: the share of each day spent running, idle
        /// and down, plus the part with no record. Today's column covers the day so far.
        /// </summary>
        private async Task RefreshStateChartAsync()
        {
            DateTime now = DateTime.Now;
            DateTime first = now.Date.AddDays(-6);

            IReadOnlyList<HiveStateChangeRecord> changes = new HiveStateChangeRecord[0];

            if (_stateHistory == null)
            {
                StateChangeNote = "There is no database, so no run, idle or downtime history.";
            }
            else
            {
                try
                {
                    changes = await _stateHistory.GetStateChangesAsync(first, now) ?? new HiveStateChangeRecord[0];
                    StateChangeNote = changes.Count == 0 ? "No run, idle or downtime recorded in the last seven days." : string.Empty;
                }
                catch (Exception ex)
                {
                    StateChangeNote = "State history could not be read: " + ex.Message;
                }
            }

            var running = new double[7];
            var idle = new double[7];
            var down = new double[7];
            var none = new double[7];

            List<HiveStateChangeRecord> ordered = changes.OrderBy(c => c.Time).ToList();

            for (int day = 0; day < 7; day++)
            {
                DateTime dayStart = first.AddDays(day);
                DateTime dayEnd = day == 6 ? now : dayStart.AddDays(1);
                double total = (dayEnd - dayStart).TotalHours;

                var hours = new Dictionary<int, double>();
                HiveStateChangeRecord current = ordered.LastOrDefault(c => c.Time <= dayStart);
                int state = current != null ? current.State : 0;
                DateTime cursor = dayStart;

                foreach (HiveStateChangeRecord change in ordered.Where(c => c.Time > dayStart && c.Time < dayEnd))
                {
                    Add(hours, state, (change.Time - cursor).TotalHours);
                    state = change.State;
                    cursor = change.Time;
                }

                Add(hours, state, (dayEnd - cursor).TotalHours);

                running[day] = Percent(hours, 1, total);
                idle[day] = Percent(hours, 2, total);
                down[day] = Percent(hours, 5, total);
                none[day] = Math.Max(0, 100 - running[day] - idle[day] - down[day]);
            }

            StateChangeSeries = new ISeries[]
            {
                StateColumn("Running", running, AudioChartPalette.Running),
                StateColumn("Idle", idle, AudioChartPalette.Idle),
                StateColumn("Downtime", down, AudioChartPalette.Downtime),
                StateColumn("No record", none, AudioChartPalette.NoRecord)
            };

            // Slide 43 labels the newest column "T (YYMMDD)" and the rest T-1 .. T-6.
            var labels = new List<string>();
            for (int offset = 6; offset >= 1; offset--)
                labels.Add("T-" + offset + " (" + now.Date.AddDays(-offset).ToString("MM/dd") + ")");
            //labels.Add("T (" + now.ToString("yyMMdd") + ")");
            labels.Add("T (" + now.ToString("MM/dd") + ")");

            StateChangeXAxes = new[]
            {
                new Axis
                {
                    Labels = labels,
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 12,
                    SeparatorsPaint = null
                }
            };

            StateChangeYAxes = new[]
            {
                new Axis
                {
                    MinLimit = 0,
                    MaxLimit = 100,
                    Labeler = value => value.ToString("0") + "%",
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 12,
                    SeparatorsPaint = AudioChartPalette.Line(AudioChartPalette.GridLine, 1)
                }
            };
        }

        private static void Add(Dictionary<int, double> hours, int state, double value)
        {
            if (value <= 0)
                return;

            double existing;
            hours.TryGetValue(state, out existing);
            hours[state] = existing + value;
        }

        private static double Percent(Dictionary<int, double> hours, int state, double total)
        {
            double value;
            return total > 0 && hours.TryGetValue(state, out value) ? Math.Round(value / total * 100, 1) : 0;
        }

        private static StackedColumnSeries<double> StateColumn(string name, double[] values, SKColor colour)
        {
            return new StackedColumnSeries<double>
            {
                Name = name,
                Values = values,
                Fill = AudioChartPalette.Fill(colour),
                Stroke = null,
                IgnoresBarPosition = true
            };
        }


        // ==================================================================
        // Error Statistics  (slide 43: "shows today's data. Past 7 days' data is
        // stored locally. Filter slider can be used to view locally stored data")
        // ==================================================================

        public ObservableCollection<ErrorStatRow> ErrorStatistics { get; } =
            new ObservableCollection<ErrorStatRow>();

        /// <summary>The slider's zero: midnight six days ago, so its range is the last seven days.</summary>
        private DateTime _errorRangeOrigin;

        /// <summary>Length of the slider, in hours: from its zero to now.</summary>
        [ObservableProperty]
        private double _errorRangeMaxHours;

        /// <summary>Left handle, hours after the slider's zero.</summary>
        [ObservableProperty]
        private double _errorRangeStartHours;

        /// <summary>Right handle, hours after the slider's zero.</summary>
        [ObservableProperty]
        private double _errorRangeEndHours;

        [ObservableProperty]
        private DateTime _errorFilterStart = DateTime.Today;

        [ObservableProperty]
        private DateTime _errorFilterEnd = DateTime.Now;

        [ObservableProperty]
        private string _errorStatisticsNote = string.Empty;

        private CancellationTokenSource _errorDebounce;
        private bool _settingErrorRange;

        partial void OnErrorRangeStartHoursChanged(double value) => OnErrorRangeMoved();

        partial void OnErrorRangeEndHoursChanged(double value) => OnErrorRangeMoved();

        /// <summary>
        /// A drag moves the handles many times a second; the alarm log is read once the
        /// operator lets go (400 ms without a change).
        /// </summary>
        private async void OnErrorRangeMoved()
        {
            ErrorFilterStart = _errorRangeOrigin.AddHours(ErrorRangeStartHours);
            ErrorFilterEnd = _errorRangeOrigin.AddHours(ErrorRangeEndHours);

            if (_settingErrorRange)
                return;

            if (_errorDebounce != null)
                _errorDebounce.Cancel();

            _errorDebounce = new CancellationTokenSource();
            CancellationToken token = _errorDebounce.Token;

            try
            {
                await Task.Delay(400, token);
                await RefreshErrorStatisticsAsync(followNow: false);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a later move.
            }
        }

        /// <param name="followNow">
        /// On the periodic refresh the slider's range grows to the new "now", and a right
        /// handle that was at the end stays at the end.
        /// </param>
        private async Task RefreshErrorStatisticsAsync(bool followNow)
        {
            if (followNow)
            {
                DateTime now = DateTime.Now;
                bool endWasNow = ErrorRangeEndHours >= ErrorRangeMaxHours - 0.01;

                _settingErrorRange = true;
                _errorRangeOrigin = now.Date.AddDays(-6);
                ErrorRangeMaxHours = Math.Round((now - _errorRangeOrigin).TotalHours, 2);
                if (endWasNow)
                    ErrorRangeEndHours = ErrorRangeMaxHours;
                _settingErrorRange = false;

                ErrorFilterStart = _errorRangeOrigin.AddHours(ErrorRangeStartHours);
                ErrorFilterEnd = _errorRangeOrigin.AddHours(ErrorRangeEndHours);
            }

            ErrorStatistics.Clear();

            if (_alarms == null)
            {
                ErrorStatisticsNote = "Alarm history is not available on this machine.";
                return;
            }

            IReadOnlyList<AlarmData> alarms;
            try
            {
                alarms = await _alarms.QueryAsync(ErrorFilterStart, ErrorFilterEnd) ?? new AlarmData[0];
            }
            catch (Exception ex)
            {
                ErrorStatisticsNote = "Alarm history could not be read: " + ex.Message;
                return;
            }

            DateTime until = DateTime.Now;
            var byType = alarms
                .GroupBy(a => ErrorTypeOf(a))
                .Select(g => new
                {
                    Name = g.Key,
                    Count = g.Count(),
                    // A standing alarm counts until now.
                    Minutes = g.Sum(a => ((a.EndTime > a.StartTime ? a.EndTime : until) - a.StartTime).TotalMinutes)
                })
                .OrderByDescending(x => x.Count)
                .ThenByDescending(x => x.Minutes)
                .ToList();

            int max = byType.Count > 0 ? byType.Max(x => x.Count) : 0;
            foreach (var type in byType)
            {
                ErrorStatistics.Add(new ErrorStatRow(type.Name, type.Count, max,
                    type.Count + " alarm(s), " + type.Minutes.ToString("0.#") + " min of downtime"));
            }

            ErrorStatisticsNote = byType.Count == 0 ? "No alarms in this window." : string.Empty;
        }


        // ==================================================================
        // I/O Summary  (slide 43: "statistics update hourly. Each unit status shows as
        // it completes processing. Failed units turn SN background red")
        // ==================================================================

        /// <summary>Serial number of the unit that most recently finished.</summary>
        [ObservableProperty]
        private string _lastUnitSn = NoValue;

        /// <summary>Drives the banner fill: DFM green #00F900 on a pass, red #EC5D57 on a fail.</summary>
        [ObservableProperty]
        private bool _lastUnitPassed = true;

        public ObservableCollection<SummaryRow> IoSummary { get; } = new ObservableCollection<SummaryRow>();

        /// <summary>"Shift DS since 08:00 · updated 10:00", for the headline's tooltip.</summary>
        [ObservableProperty]
        private string _ioSummaryCaption = string.Empty;

        /// <summary>"10:00" - when the statistics were last worked out, beside the headline.</summary>
        [ObservableProperty]
        private string _ioSummaryUpdated = string.Empty;

        private SummaryRow _inputOutput;
        private SummaryRow _yield;
        private SummaryRow _passFail;
        private SummaryRow _uph;
        private SummaryRow _cycleTime;

        /// <summary>
        /// The shift so far: input/output, yield, pass/fail, UPH and average unit CT.
        /// </summary>
        private async Task RefreshIoSummaryAsync()
        {
            if (_production == null)
            {
                IoSummaryCaption = "No production record on this machine.";
                return;
            }

            DateTime now = DateTime.Now;
            DateTime from = now.Date;
            string shiftName = null;

            try
            {
                ShiftProduction shift = await _production.GetShiftHourlyProductionAsync();
                if (shift != null && shift.HasShift)
                {
                    from = shift.WindowStart;
                    shiftName = shift.ShiftName;
                }

                IReadOnlyList<ProductionUnitRecord> records =
                    await _production.GetProductionRecordsAsync(from, now) ?? new ProductionUnitRecord[0];

                int total = records.Count;
                int pass = records.Count(r => r.IsPass);
                double hours = Math.Max((now - from).TotalHours, 1.0 / 60);

                _inputOutput.Value = total.ToString("N0") + "/" + pass.ToString("N0");
                _yield.Value = total > 0 ? ((double)pass / total * 100).ToString("0.0") + "%" : NoValue;
                _passFail.Value = pass.ToString("N0") + "/" + (total - pass).ToString("N0");
                _uph.Value = (total / hours).ToString("N0");
                _cycleTime.Value = total > 0 ? records.Average(r => r.CycleTimeSeconds).ToString("0.00") + " s" : NoValue;

                IoSummaryCaption = (shiftName != null ? "Shift " + shiftName + " since " : "Since ")
                                   + from.ToString("HH:mm") + " · updated " + now.ToString("HH:mm")
                                   + " · the statistics update hourly";
                IoSummaryUpdated = now.ToString("HH:mm");
            }
            catch (Exception ex)
            {
                IoSummaryCaption = "Production record could not be read: " + ex.Message;
            }
        }

        /// <summary>The newest unit, checked every ten seconds so it shows as it completes.</summary>
        private async Task RefreshLastUnitAsync()
        {
            if (_production == null)
                return;

            try
            {
                DateTime now = DateTime.Now;
                IReadOnlyList<ProductionUnitRecord> recent =
                    await _production.GetProductionRecordsAsync(now.AddMinutes(-30), now) ?? new ProductionUnitRecord[0];

                ProductionUnitRecord newest = recent.OrderByDescending(r => r.OutputTime).FirstOrDefault();
                if (newest == null)
                    return;

                LastUnitSn = string.IsNullOrWhiteSpace(newest.ProductCode) ? NoValue : newest.ProductCode;
                LastUnitPassed = newest.IsPass;
            }
            catch (Exception)
            {
                // Keep the last unit shown; the I/O caption reports a failing record.
            }
        }


        // ==================================================================
        // Parameter Dashboard  (slide 43: "Follow AME definition on critical params.
        // Show OOS params at top of list in red", "Use N/A in LSL/USL if there is no
        // spec limit", "Show current glue SN with no limits (dispenser only)")
        // ==================================================================

        public ObservableCollection<ParameterRow> Parameters { get; } = new ObservableCollection<ParameterRow>();

        /// <summary>The Spec Limits row a unit's picture count is checked against (T5).</summary>
        private const string PictureQuantityParameter = "Pic_Upload_quantity";

        [ObservableProperty]
        private string _parameterNote = string.Empty;

        /// <summary>
        /// One row per production spec limit (Config &gt; Spec Limits, slide 58), with its
        /// live value; out-of-spec rows first; then the glue SN of each gantry.
        /// </summary>
        private async Task RefreshParametersAsync()
        {
            string error = null;
            IReadOnlyList<AeLimitRecord> limits = new AeLimitRecord[0];
            if (_settings != null)
            {
                try
                {
                    limits = _settings.LoadAeLimits(AeLimitKind.Production) ?? new AeLimitRecord[0];
                }
                catch (Exception ex)
                {
                    error = "Spec limits could not be read: " + ex.Message;
                }
            }

            IReadOnlyDictionary<string, double> values = new Dictionary<string, double>();
            if (_parameterValues != null && limits.Count > 0)
            {
                try
                {
                    values = await _parameterValues.ReadAsync(limits.Select(l => l.Parameter).ToList())
                             ?? new Dictionary<string, double>();
                }
                catch (Exception ex)
                {
                    error = "Parameter values could not be read: " + ex.Message;
                }
            }

            // T5: the picture count of the last unit is the first row, against the
            // Pic_Upload_quantity limits it was checked with.
            PictureUploadRecord lastPictures = null;
            if (_pictures != null)
            {
                try
                {
                    lastPictures = _pictures.Recent(1).FirstOrDefault();
                }
                catch (Exception)
                {
                    // The dashboard still shows the parameters.
                }
            }

            var rows = new List<ParameterRow>();
            ParameterRow pictureRow = null;
            foreach (AeLimitRecord limit in limits.Where(l => !string.IsNullOrWhiteSpace(l.Parameter)))
            {
                double value;
                string text = values.TryGetValue(limit.Parameter, out value) ? value.ToString("0.###") : NoValue;
                string key = UiLanguage.SpecLimit(limit.Parameter.Trim()) + (string.IsNullOrWhiteSpace(limit.Unit) ? string.Empty : " (" + limit.Unit.Trim() + ")");

                if (_pictures != null && string.Equals(limit.Parameter.Trim(), PictureQuantityParameter, StringComparison.OrdinalIgnoreCase))
                {
                    pictureRow = new ParameterRow(key, lastPictures != null ? lastPictures.Quantity.ToString(CultureInfo.InvariantCulture) : NoValue,
                        ParseLimit(limit.Min), ParseLimit(limit.Max));
                    continue;
                }

                rows.Add(new ParameterRow(key, text, ParseLimit(limit.Min), ParseLimit(limit.Max)));
            }

            if (pictureRow == null && lastPictures != null)
                pictureRow = new ParameterRow(PictureQuantityParameter, lastPictures.Quantity.ToString(CultureInfo.InvariantCulture), lastPictures.Lsl, lastPictures.Usl);

            // S3.3: MES's nozzle health of each gantry (REMAINING_TIME against MAINTAIN_TIME,
            // hours). Shown here only - never sent to MES or HIVE.
            var healthRows = new List<ParameterRow>();
            if (_consumables != null)
            {
                try
                {
                    foreach (GantryConsumables gantry in _consumables.Gantries)
                    {
                        if (!gantry.NozzleRemainingHours.HasValue)
                            continue;

                        healthRows.Add(new ParameterRow(gantry.Gantry + "_Health_Status",
                            gantry.NozzleRemainingHours.Value.ToString("0.###"), 0, gantry.NozzleMaintainHours));
                    }
                }
                catch (Exception)
                {
                    // The dashboard still shows the parameters.
                }
            }

            var glueRows = new List<ParameterRow>();
            if (_dispenser != null)
            {
                try
                {
                    foreach (GlueCartridgeStatus cartridge in await _dispenser.GetGlueCartridgesAsync() ?? new GlueCartridgeStatus[0])
                        glueRows.Add(new ParameterRow("Glue SN (" + cartridge.Gantry + ")", cartridge.GlueSn ?? NoValue, null, null));
                }
                catch (Exception)
                {
                    // The dashboard still shows the parameters.
                }
            }

            Parameters.Clear();
            if (pictureRow != null)
                Parameters.Add(pictureRow);
            foreach (ParameterRow row in rows.Concat(healthRows).OrderByDescending(r => r.IsOutOfSpec).Concat(glueRows))
                Parameters.Add(row);

            ParameterNote = error
                ?? (limits.Count == 0 ? "No production spec limits are set (Config > Spec Limits)."
                    : _parameterValues == null ? "Limits from Config > Spec Limits. Live values need the PLC parameter link, which is not connected yet."
                    : "Limits from Config > Spec Limits · updated " + DateTime.Now.ToString("HH:mm:ss"));
        }

        private static double? ParseLimit(string text)
        {
            double value;
            return double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : (double?)null;
        }


        // ==================================================================

        /// <summary>
        /// Builds the empty panels and the I/O rows, so the page renders at once; the
        /// refresh loop fills them.
        /// </summary>
        private void InitialiseDashboard()
        {
            _inputOutput = new SummaryRow("Input/Output", NoValue);
            _yield = new SummaryRow("Yield", NoValue);
            _passFail = new SummaryRow("Pass/Fail", NoValue);
            _uph = new SummaryRow("UPH", NoValue);
            _cycleTime = new SummaryRow("CT", NoValue);

            IoSummary.Add(_inputOutput);
            IoSummary.Add(_yield);
            IoSummary.Add(_passFail);
            IoSummary.Add(_uph);
            IoSummary.Add(_cycleTime);

            // Error Statistics shows today by default (slide 43), inside a seven-day range.
            DateTime now = DateTime.Now;
            _errorRangeOrigin = now.Date.AddDays(-6);
            _settingErrorRange = true;
            ErrorRangeMaxHours = Math.Round((now - _errorRangeOrigin).TotalHours, 2);
            ErrorRangeStartHours = 6 * 24;
            ErrorRangeEndHours = ErrorRangeMaxHours;
            _settingErrorRange = false;
            ErrorFilterStart = _errorRangeOrigin.AddHours(ErrorRangeStartHours);
            ErrorFilterEnd = now;
        }
    }
}
