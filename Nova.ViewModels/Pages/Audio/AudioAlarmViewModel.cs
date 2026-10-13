using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Kernel;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using Nova.Abstractions.Data;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Data.Repositories;
using Nova.Abstractions.Services;
using Nova.ViewModels.Audio;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The Alarm page - Audio SW DFM v5.4 slide 56.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Layout the slide dimensions: a time filter row with Query and Save across the
    /// top, then a half-height band holding the Top 10 downtime breakdown (table plus
    /// pie) beside the alarm-duration histogram, then a half-height alarm table.
    /// </para>
    /// <para>
    /// This page is fed by real data - <see cref="IAlarmLogRepository"/> reads the
    /// persisted [Alarms] rows, and the Top 10, the pie and the histogram are all
    /// derived from that same result set rather than queried separately, so the three
    /// panels can never disagree about the window they are showing.
    /// </para>
    /// </remarks>
    public partial class AudioAlarmViewModel : AudioPageViewModel
    {
        public override AppPageNames PageName => AppPageNames.Alarm;

        /// <summary>
        /// True when the alarm log generates its rows (SampleData=true). The view labels
        /// the page, so a generated alarm history is never taken for the machine's.
        /// </summary>
        public bool IsSampleData => _alarms is ISampleDataSource;

        private readonly IAlarmLogRepository _alarms;

        /// <summary>
        /// PLC code to DFM code and error type (slide 56: "Follow error code definition",
        /// "Follow error type definition"). Null for sample data, which is written in DFM
        /// codes already, and when no error code file is configured.
        /// </summary>
        private readonly IErrorCodeCatalog _errorCodes;
        private readonly ICsvExportService _csv;
        private readonly IDialogService _dialog;


        // ==================================================================
        // Filter row
        // ==================================================================

        /// <summary>
        /// The window the page opens with: "By default, the statistics of the last 48
        /// hours (00:00 of the previous day to 24:00 of the current day)" - the Alarm
        /// slide's key requirements. So: yesterday 00:00 to the end of today.
        /// </summary>
        [ObservableProperty]
        private DateTime _startTime = DateTime.Today.AddDays(-1);

        [ObservableProperty]
        private DateTime _endTime = DateTime.Today.AddDays(1);

        [ObservableProperty]
        private bool _isBusy;

        /// <summary>
        /// What the panels are currently showing - the queried window, "no records", or
        /// why the query failed. Shown in the filter row rather than only in a dialog,
        /// so an empty page always says why it is empty.
        /// </summary>
        [ObservableProperty]
        private string _statusText = "Choose a time range and press Query.";


        // ==================================================================
        // Downtime Statistic (Top 10)
        // ==================================================================

        public ObservableCollection<DowntimeStatRow> DowntimeTop10 { get; } =
            new ObservableCollection<DowntimeStatRow>();

        [ObservableProperty]
        private ISeries[] _downtimePieSeries = new ISeries[0];


        // ==================================================================
        // Alarm durations histogram
        // ==================================================================

        [ObservableProperty]
        private ISeries[] _durationSeries = new ISeries[0];

        [ObservableProperty]
        private Axis[] _durationXAxes = new Axis[0];

        [ObservableProperty]
        private Axis[] _durationYAxes = new Axis[0];


        // ==================================================================
        // Alarm history table
        // ==================================================================

        public ObservableCollection<AlarmHistoryRow> AlarmRows { get; } =
            new ObservableCollection<AlarmHistoryRow>();


        // ==================================================================
        // Commands
        // ==================================================================

        public IAsyncRelayCommand QueryCommand { get; }

        public IRelayCommand SaveCommand { get; }

        private bool CanSave()
        {
            return !IsBusy && AlarmRows.Count > 0;
        }

        partial void OnIsBusyChanged(bool value)
        {
            SaveCommand.NotifyCanExecuteChanged();
            QueryCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Reads the window and rebuilds all three panels from the one result set.
        /// </summary>
        private async Task QueryAsync()
        {
            if (_alarms == null)
            {
                StatusText = "No alarm log is configured on this machine.";
                return;
            }

            if (EndTime < StartTime)
            {
                StatusText = "End time is before start time.";
                return;
            }

            IsBusy = true;
            try
            {
                IReadOnlyList<AlarmData> records = await _alarms.QueryAsync(StartTime, EndTime);
                Apply(records ?? new AlarmData[0]);

                StatusText = AlarmRows.Count == 0
                    ? "No alarms between " + StartTime.ToString("dd/MM/yyyy HH:mm") +
                      " and " + EndTime.ToString("dd/MM/yyyy HH:mm") + "."
                    : AlarmRows.Count.ToString("N0") + " alarms between " +
                      StartTime.ToString("dd/MM/yyyy HH:mm") + " and " +
                      EndTime.ToString("dd/MM/yyyy HH:mm") + ".";
            }
            catch (Exception ex)
            {
                // A failed query must not leave the previous window's figures on screen
                // looking like the new window's.
                Apply(new AlarmData[0]);
                StatusText = "Query failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                SaveCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>
        /// Rebuilds the table, the Top 10 breakdown, the pie and the histogram from one
        /// set of records.
        /// </summary>
        private void Apply(IReadOnlyList<AlarmData> records)
        {
            AlarmRows.Clear();
            DowntimeTop10.Clear();

            records = records.Select(Translate).ToList();

            foreach (AlarmData record in records.OrderByDescending(r => r.StartTime))
                AlarmRows.Add(ToRow(record));

            BuildTop10(records);
            BuildDurationHistogram(records);
        }

        /// <summary>
        /// One alarm as the slide's seven columns.
        /// </summary>
        /// <remarks>
        /// An alarm that has not cleared has no end time, and the slide's first row
        /// shows it as "Ongoing" with a blank End Time. A default(DateTime) is what the
        /// repository returns for that case, so it is detected here rather than being
        /// formatted into a misleading "0001-01-01" and a zero duration.
        /// </remarks>
        /// <summary>
        /// The alarm as HIVE knows it: DFM code and error type, with the machine's own text
        /// kept as the detail ("Show original errors reported by machine"). The Top 10,
        /// the pie and the Save file are then all in DFM terms too.
        /// </summary>
        private AlarmData Translate(AlarmData record)
        {
            if (_errorCodes == null || record == null)
                return record;

            DfmError dfm = _errorCodes.Translate(record.AlarmCode, record.AlarmDescription);
            return new AlarmData
            {
                StartTime = record.StartTime,
                EndTime = record.EndTime,
                AlarmCode = dfm.Code,
                AlarmType = dfm.Message,
                AlarmDescription = string.IsNullOrWhiteSpace(record.AlarmCode)
                    ? dfm.Detail
                    : dfm.Detail + " [PLC " + record.AlarmCode + "]",
                Shift = record.Shift,
            };
        }

        private static AlarmHistoryRow ToRow(AlarmData record)
        {
            bool ongoing = record.EndTime == default(DateTime) || record.EndTime < record.StartTime;

            string timeInState = ongoing
                ? "Ongoing"
                : (record.EndTime - record.StartTime).TotalMinutes.ToString("0");

            return new AlarmHistoryRow(
                record.StartTime.ToString("yyyy-MM-dd"),
                record.StartTime.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                ongoing ? string.Empty : record.EndTime.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                timeInState,
                record.AlarmCode,
                record.AlarmType,
                record.AlarmDescription);
        }

        /// <summary>
        /// The ten error types that cost the most downtime in the window, largest
        /// first, plus the matching pie.
        /// </summary>
        /// <remarks>
        /// Ranked by accumulated minutes rather than by count: the slide's table leads
        /// with "Total Time (Min)" and the pie is drawn from it, so an error that fires
        /// twice for an hour each outranks one that fires fifty times for a second.
        /// Still-open alarms contribute nothing, because their duration is not known
        /// yet and assuming "until now" would inflate whichever error happens to be
        /// standing when the operator presses Query.
        /// </remarks>
        private void BuildTop10(IReadOnlyList<AlarmData> records)
        {
            var byType = records
                .Where(r => r.EndTime != default(DateTime) && r.EndTime >= r.StartTime)
                .GroupBy(r => string.IsNullOrWhiteSpace(r.AlarmType) ? "Unclassified" : r.AlarmType)
                .Select(g => new
                {
                    Name = g.Key,
                    Minutes = g.Sum(r => (r.EndTime - r.StartTime).TotalMinutes),
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Minutes)
                .Take(10)
                .ToList();

            var slices = new List<ISeries>();

            for (int i = 0; i < byType.Count; i++)
            {
                DowntimeTop10.Add(new DowntimeStatRow(i, byType[i].Name, byType[i].Minutes, byType[i].Count));

                slices.Add(new PieSeries<double>
                {
                    Name = byType[i].Name,
                    Values = new[] { byType[i].Minutes },
                    Fill = AudioChartPalette.Fill(AudioChartPalette.TopN(i)),
                    Stroke = null,
                    // Slide 56 prints the share on each slice (35%, 29%, 11%, 10%, 8%,
                    // 7%). Only the percentage, not the error name: the name is already
                    // in the table beside the pie, tied to the slice by its colour
                    // swatch, and repeating it here is what would crowd the chart.
                    DataLabelsPaint = AudioChartPalette.Fill(AudioChartPalette.Stroke),
                    DataLabelsSize = 13,
                    DataLabelsPosition = PolarLabelsPosition.Middle,
                    // Share is the slice's fraction of the pie total, which is what the
                    // slide's percentages are - not the raw minutes this series carries.
                    DataLabelsFormatter = point => FormatShare(point.StackedValue)
                });
            }

            DowntimePieSeries = slices.ToArray();
        }

        /// <summary>
        /// The slice's share of the pie, as the whole-number percentage slide 56 prints.
        /// </summary>
        /// <remarks>
        /// Blank rather than "0%" for a slice too small to round to one percent: a
        /// zero next to a slice that is visibly present reads as a bug. Blank when the
        /// stacked total is missing or zero for the same reason - a share cannot be
        /// computed from nothing, and showing one anyway would be inventing it.
        /// </remarks>
        private static string FormatShare(StackedValue stacked)
        {
            if (stacked == null || stacked.Total <= 0)
                return string.Empty;

            double percent = stacked.Share * 100.0;
            return percent < 0.5 ? string.Empty : percent.ToString("0") + "%";
        }

        /// <summary>
        /// Five-minute buckets, per the slide's "X axis: statistics in minutes, Example,
        /// &lt; 5 minutes are classified as (0-4)" and "Statistics of times" - the bar
        /// height is how MANY alarms fell in the bucket, not how long they lasted.
        /// </summary>
        private void BuildDurationHistogram(IReadOnlyList<AlarmData> records)
        {
            string[] labels = { "0--4", "5--9", "10--14", "15--19", "20--24", "25+" };
            var counts = new int[labels.Length];

            foreach (AlarmData record in records)
            {
                if (record.EndTime == default(DateTime) || record.EndTime < record.StartTime)
                    continue;

                double minutes = (record.EndTime - record.StartTime).TotalMinutes;
                int bucket = (int)(minutes / 5);
                if (bucket >= counts.Length)
                    bucket = counts.Length - 1;   // everything from 25 minutes up shares the last bar
                if (bucket < 0)
                    bucket = 0;

                counts[bucket]++;
            }

            DurationSeries = new ISeries[]
            {
                new ColumnSeries<int>
                {
                    Name = "Alarms",
                    Values = counts,
                    Fill = AudioChartPalette.Fill(AudioChartPalette.Downtime),
                    Stroke = null,
                    DataLabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    DataLabelsSize = 11,
                    DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top
                }
            };

            DurationXAxes = new[]
            {
                new Axis
                {
                    Labels = labels,
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 12,
                    SeparatorsPaint = null,
                    Name = "Alarm Durations (min)",
                    NamePaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    NameTextSize = 12
                }
            };

            DurationYAxes = new[]
            {
                new Axis
                {
                    MinLimit = 0,
                    // Whole alarms only - a "2.5 alarms" tick would be nonsense.
                    MinStep = 1,
                    Labeler = value => value.ToString("0"),
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 12,
                    SeparatorsPaint = AudioChartPalette.Line(AudioChartPalette.GridLine, 1)
                }
            };
        }

        /// <summary>
        /// Slide 56's Save button. Writes the alarm table exactly as displayed.
        /// </summary>
        private void Save()
        {
            if (_csv == null)
            {
                if (_dialog != null)
                    _dialog.ShowWarning("CSV export is not available on this machine.", "Save");
                return;
            }

            var headers = new[]
            {
                "Date", "Start Time", "End Time", "Time in State (Min)",
                "Error Code", "Error Message", "Error Detail"
            };

            var rows = AlarmRows
                .Select(r => (IReadOnlyList<string>)new[]
                {
                    r.Date, r.StartTime, r.EndTime, r.TimeInStateText,
                    r.ErrorCode, r.ErrorMessage, r.ErrorDetail
                })
                .ToList();

            _csv.Export(headers, rows, "Alarms_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
        }

        public AudioAlarmViewModel(
            IAppSession session,
            IAlarmLogRepository alarms = null,
            ICsvExportService csv = null,
            IDialogService dialog = null,
            IErrorCodeCatalog errorCodes = null)
            : base(session)
        {
            _alarms = alarms;
            _errorCodes = alarms is ISampleDataSource ? null : errorCodes;
            _csv = csv;
            _dialog = dialog;

            QueryCommand = new AsyncRelayCommand(QueryAsync, () => !IsBusy);
            SaveCommand = new RelayCommand(Save, CanSave);

            // Empty axes would render as a blank box rather than an empty chart, so the
            // histogram is built once up front against no records.
            BuildDurationHistogram(new AlarmData[0]);
        }

        /// <summary>
        /// Slide 56 shows the page already populated, so the first visit runs the
        /// default query (today) rather than making the operator press Query to see
        /// anything at all.
        /// </summary>
        public override void OnNavigatedTo()
        {
            base.OnNavigatedTo();

            if (!_hasQueried && QueryCommand.CanExecute(null))
            {
                _hasQueried = true;
                QueryCommand.Execute(null);
            }
        }

        private bool _hasQueried;
    }
}
