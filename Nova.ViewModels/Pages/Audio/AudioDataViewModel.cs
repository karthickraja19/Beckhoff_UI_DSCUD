using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Nova.Abstractions.Data;
using Nova.Abstractions.Data.Repositories;
using Nova.Abstractions.Services;
using Nova.ViewModels.Audio;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The Data page - Audio SW DFM v5.4 slides 59 (I/O) and 60 (Tossing).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The slide's three key requirements are Filter time, IO/Tossing and Data Export.
    /// Layout is 4/5 width of charts and the unit table, 1/5 width of summary column
    /// with the Hourly/Daily switch and Export.
    /// </para>
    /// <para>
    /// I/O and Tossing are the same page with different series, which is why one view
    /// model serves both: switching <see cref="SelectedView"/> re-projects the SAME
    /// queried records rather than re-querying, so the two views can never show
    /// different windows.
    /// </para>
    /// <para>
    /// Tossing is PnP/Screw only (slide 60's title). The tab stays visible on other
    /// machine types because nothing here knows the machine type yet; its counters read
    /// zero, which is truthful for a machine that does not toss.
    /// </para>
    /// </remarks>
    public partial class AudioDataViewModel : AudioPageViewModel
    {
        public override AppPageNames PageName => AppPageNames.Data;

        private readonly IProductionRepository _production;
        private readonly ICsvExportService _csv;
        private readonly IDialogService _dialog;

        private IReadOnlyList<ProductionUnitRecord> _records = new ProductionUnitRecord[0];


        // ==================================================================
        // I/O | Tossing  and  Hourly | Daily
        // ==================================================================

        public ObservableCollection<string> Views { get; } =
            new ObservableCollection<string> { "I/O", "Tossing" };

        [ObservableProperty]
        private string _selectedView = "I/O";

        public bool IsTossingView => SelectedView == "Tossing";

        partial void OnSelectedViewChanged(string value)
        {
            OnPropertyChanged(nameof(IsTossingView));
            Project();
        }

        public ObservableCollection<string> Buckets { get; } =
            new ObservableCollection<string> { "Hourly", "Daily" };

        [ObservableProperty]
        private string _selectedBucket = "Hourly";

        public bool IsDaily => SelectedBucket == "Daily";

        partial void OnSelectedBucketChanged(string value)
        {
            OnPropertyChanged(nameof(IsDaily));

            // Daily covers a week, hourly covers a day. Moving the window is the point
            // of the switch, so the range follows it and the data is re-read.
            EndTime = DateTime.Now;
            StartTime = IsDaily ? DateTime.Today.AddDays(-6) : DateTime.Today;

            if (RefreshCommand.CanExecute(null))
                RefreshCommand.Execute(null);
        }


        // ==================================================================
        // Time filter
        // ==================================================================

        [ObservableProperty]
        private DateTime _startTime = DateTime.Today;

        [ObservableProperty]
        private DateTime _endTime = DateTime.Now;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusText = string.Empty;


        // ==================================================================
        // Charts
        // ==================================================================

        /// <summary>Cycle-time line, top chart. Slide 59: DS Output_CT / NS Output_CT.</summary>
        [ObservableProperty]
        private ISeries[] _rateSeries = new ISeries[0];

        /// <summary>Output (or tossing) bars, bottom chart. Slide 59: DS Output / NS Output.</summary>
        [ObservableProperty]
        private ISeries[] _volumeSeries = new ISeries[0];

        [ObservableProperty]
        private Axis[] _sharedXAxes = new Axis[0];

        [ObservableProperty]
        private Axis[] _rateYAxes = new Axis[0];

        [ObservableProperty]
        private Axis[] _volumeYAxes = new Axis[0];

        /// <summary>Legend text for the top chart, which differs between the two views.</summary>
        public string RateSeriesLabel => IsTossingView ? "Tossing rate %" : "Cycle time (s)";

        public string VolumeSeriesLabel => IsTossingView ? "Tossed units" : "Units out";


        // ==================================================================
        // Unit table
        // ==================================================================

        public ObservableCollection<UnitDataRow> Units { get; } =
            new ObservableCollection<UnitDataRow>();


        // ==================================================================
        // Summary column
        // ==================================================================

        [ObservableProperty]
        private string _lastUnitSn = "—";

        [ObservableProperty]
        private bool _lastUnitPassed = true;

        public ObservableCollection<SummaryRow> IoSummary { get; } =
            new ObservableCollection<SummaryRow>();

        public ObservableCollection<SummaryRow> TossingSummary { get; } =
            new ObservableCollection<SummaryRow>();


        // ==================================================================
        // Commands
        // ==================================================================

        public IAsyncRelayCommand RefreshCommand { get; }

        public IRelayCommand ExportCommand { get; }

        private bool CanExport()
        {
            return !IsBusy && Units.Count > 0;
        }

        partial void OnIsBusyChanged(bool value)
        {
            RefreshCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
        }

        private async Task RefreshAsync()
        {
            if (_production == null)
            {
                StatusText = "No production record is configured on this machine.";
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
                _records = await _production.GetProductionRecordsAsync(StartTime, EndTime)
                           ?? new ProductionUnitRecord[0];

                Project();

                StatusText = _records.Count == 0
                    ? "No units between " + StartTime.ToString("dd/MM/yyyy HH:mm") +
                      " and " + EndTime.ToString("dd/MM/yyyy HH:mm") + "."
                    : _records.Count.ToString("N0") + " units between " +
                      StartTime.ToString("dd/MM/yyyy HH:mm") + " and " +
                      EndTime.ToString("dd/MM/yyyy HH:mm") + ".";
            }
            catch (Exception ex)
            {
                _records = new ProductionUnitRecord[0];
                Project();
                StatusText = "Query failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                ExportCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>
        /// Re-derives every panel from <see cref="_records"/>. Called on a new query and
        /// on an I/O-to-Tossing switch, so the two views are always two projections of
        /// one result set.
        /// </summary>
        private void Project()
        {
            BuildUnitTable();
            BuildCharts();
            BuildSummaries();

            OnPropertyChanged(nameof(RateSeriesLabel));
            OnPropertyChanged(nameof(VolumeSeriesLabel));
        }

        private void BuildUnitTable()
        {
            Units.Clear();

            // Newest first, and capped: the DFM table is a viewer, not an export, and a
            // full shift of units would push tens of thousands of rows into the visual
            // tree. Export writes the whole set.
            const int maxRows = 500;

            foreach (ProductionUnitRecord record in _records
                         .OrderByDescending(r => r.ProductionTime)
                         .Take(maxRows))
            {
                Units.Add(new UnitDataRow(
                    record.Date.ToString("yyyy/MM/dd"),
                    record.Shift,
                    record.ProductCode,
                    "N/A",
                    record.ProductionTime.ToString("HH:mm:ss.fff"),
                    record.ProductionTime.AddSeconds(record.CycleTimeSeconds).ToString("HH:mm:ss.fff"),
                    record.IsPass,
                    record.CycleTimeSeconds,
                    record.IsPass ? 1 : 5,
                    // "Tossing" in the DFM is the retest index - iteration 1 is a first
                    // pass, anything above it is a retest of the same unit.
                    Math.Max(0, record.Iteration - 1)));
            }

            ProductionUnitRecord newest = _records.OrderByDescending(r => r.ProductionTime).FirstOrDefault();
            LastUnitSn = newest != null && !string.IsNullOrWhiteSpace(newest.ProductCode)
                ? newest.ProductCode
                : "—";
            LastUnitPassed = newest == null || newest.IsPass;

            if (_records.Count > maxRows)
                StatusText = "Showing the newest " + maxRows.ToString("N0") + " of " +
                             _records.Count.ToString("N0") + " units. Export writes all of them.";
        }

        /// <summary>
        /// Buckets the records by hour or by day and builds the two charts.
        /// </summary>
        private void BuildCharts()
        {
            // Group on the bucket start, so an empty hour simply has no point rather
            // than shifting later buckets left.
            var groups = _records
                .GroupBy(r => IsDaily
                    ? r.ProductionTime.Date
                    : new DateTime(r.ProductionTime.Year, r.ProductionTime.Month, r.ProductionTime.Day,
                                   r.ProductionTime.Hour, 0, 0))
                .OrderBy(g => g.Key)
                .ToList();

            var labels = groups
                .Select(g => IsDaily ? g.Key.ToString("yyyy/MM/dd") : g.Key.ToString("HH:mm"))
                .ToList();

            double[] rate;
            double[] volume;

            if (IsTossingView)
            {
                // Tossing rate: share of units in the bucket that were retested at all.
                rate = groups
                    .Select(g => g.Count() == 0 ? 0d : (double)g.Count(r => r.Iteration > 1) / g.Count() * 100d)
                    .ToArray();
                volume = groups.Select(g => (double)g.Count(r => r.Iteration > 1)).ToArray();
            }
            else
            {
                rate = groups
                    .Select(g => g.Any() ? g.Average(r => r.CycleTimeSeconds) : 0d)
                    .ToArray();
                volume = groups.Select(g => (double)g.Count()).ToArray();
            }

            RateSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Name = RateSeriesLabel,
                    Values = rate,
                    Stroke = AudioChartPalette.Line(AudioChartPalette.SeriesPrimary, 2),
                    Fill = null,
                    GeometrySize = 6,
                    GeometryStroke = AudioChartPalette.Line(AudioChartPalette.SeriesPrimary, 2),
                    GeometryFill = AudioChartPalette.Fill(AudioChartPalette.Stroke)
                }
            };

            VolumeSeries = new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Name = VolumeSeriesLabel,
                    Values = volume,
                    Fill = AudioChartPalette.Fill(AudioChartPalette.SeriesPrimary),
                    Stroke = null
                }
            };

            SharedXAxes = new[]
            {
                new Axis
                {
                    Labels = labels,
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 11,
                    // A day of hourly buckets is 24 labels; rotating keeps them readable
                    // instead of letting the chart drop every other one.
                    LabelsRotation = labels.Count > 12 ? 45 : 0,
                    SeparatorsPaint = null
                }
            };

            RateYAxes = new[]
            {
                new Axis
                {
                    MinLimit = 0,
                    Labeler = value => value.ToString(IsTossingView ? "0'%'" : "0.0"),
                    LabelsPaint = AudioChartPalette.Fill(AudioChartPalette.AxisLine),
                    TextSize = 11,
                    SeparatorsPaint = AudioChartPalette.Line(AudioChartPalette.GridLine, 1)
                }
            };

            VolumeYAxes = new[]
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

        /// <summary>
        /// The two summary blocks in the 1/5 column (slides 59 and 60).
        /// </summary>
        /// <remarks>
        /// Rebuilt wholesale rather than updated in place, unlike the Home page's, because
        /// nothing raises these from a background thread - they only change when the
        /// operator queries or switches view, both of which are UI-thread events.
        /// </remarks>
        private void BuildSummaries()
        {
            int total = _records.Count;
            int pass = _records.Count(r => r.IsPass);
            int fail = total - pass;
            int retested = _records.Count(r => r.Iteration > 1);

            double hours = (EndTime - StartTime).TotalHours;
            double uph = hours > 0 ? total / hours : 0;
            double cycle = total > 0 ? _records.Average(r => r.CycleTimeSeconds) : 0;
            double yield = total > 0 ? (double)pass / total * 100d : 0;

            IoSummary.Clear();
            IoSummary.Add(new SummaryRow("Input/Output", total.ToString("N0") + "/" + pass.ToString("N0")));
            IoSummary.Add(new SummaryRow("Yield", yield.ToString("0.0") + "%"));
            IoSummary.Add(new SummaryRow("Pass/Fail", pass.ToString("N0") + "/" + fail.ToString("N0")));
            IoSummary.Add(new SummaryRow("UPH", uph.ToString("N0")));
            IoSummary.Add(new SummaryRow("CT", cycle.ToString("0.00") + " s"));

            // Slide 59 lists five named tossing causes. Only the total is derivable from
            // [ProductionData] - the per-cause split comes from the tossing payload the
            // machine reports to HIVE, which this build does not collect. The named rows
            // are shown as not-recorded rather than as zero, because a zero here would
            // read as "no pick-up failures" when it means "not measured".
            TossingSummary.Clear();
            TossingSummary.Add(new SummaryRow("Retested units", retested.ToString("N0")));
            TossingSummary.Add(new SummaryRow("Tossing rate",
                total > 0 ? ((double)retested / total * 100d).ToString("0.0") + "%" : "0.0%"));
            TossingSummary.Add(new SummaryRow("Material pick-up failure", "not recorded"));
            TossingSummary.Add(new SummaryRow("Lower CCD positioning NG", "not recorded"));
            TossingSummary.Add(new SummaryRow("Top CCD positioning NG", "not recorded"));
            TossingSummary.Add(new SummaryRow("Positioning NG in the tray", "not recorded"));
            TossingSummary.Add(new SummaryRow("Dropped when removing liner", "not recorded"));
        }

        /// <summary>
        /// Slide 59's Export button. Writes every queried unit, not just the capped
        /// on-screen page.
        /// </summary>
        private void Export()
        {
            if (_csv == null)
            {
                if (_dialog != null)
                    _dialog.ShowWarning("CSV export is not available on this machine.", "Export");
                return;
            }

            var headers = new[]
            {
                "Date", "Shift", "Unit SN", "Component SN", "Start Time", "End Time",
                "Pass", "CT/s", "HIVE State", "Tossing"
            };

            var rows = _records
                .OrderByDescending(r => r.ProductionTime)
                .Select(r => (IReadOnlyList<string>)new[]
                {
                    r.Date.ToString("yyyy/MM/dd"),
                    r.Shift,
                    r.ProductCode,
                    "N/A",
                    r.ProductionTime.ToString("HH:mm:ss.fff"),
                    r.ProductionTime.AddSeconds(r.CycleTimeSeconds).ToString("HH:mm:ss.fff"),
                    r.IsPass ? "TRUE" : "FALSE",
                    r.CycleTimeSeconds.ToString("0.00"),
                    r.IsPass ? "1" : "5",
                    Math.Max(0, r.Iteration - 1).ToString()
                })
                .ToList();

            _csv.Export(headers, rows,
                (IsTossingView ? "Tossing_" : "UnitData_") + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
        }

        public AudioDataViewModel(
            IAppSession session,
            IProductionRepository production = null,
            ICsvExportService csv = null,
            IDialogService dialog = null)
            : base(session)
        {
            _production = production;
            _csv = csv;
            _dialog = dialog;

            RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
            ExportCommand = new RelayCommand(Export, CanExport);

            // Build empty charts up front so the page renders as an empty chart rather
            // than a blank box before the first query returns.
            Project();
        }

        public override void OnNavigatedTo()
        {
            base.OnNavigatedTo();

            if (!_hasQueried && RefreshCommand.CanExecute(null))
            {
                _hasQueried = true;
                RefreshCommand.Execute(null);
            }
        }

        private bool _hasQueried;
    }
}
