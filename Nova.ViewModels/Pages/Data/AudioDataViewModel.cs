using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data;
using Nova.Abstractions.Data.Repositories;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The Data page - Audio SW DFM v5.4 slides 59 (I/O) and 60 (Tossing), with the
    /// Yield, Health and Glue tabs of S2.4.4 Data page X4 for a dispenser.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The slide's three key requirements are Filter time, IO/Tossing and Data Export.
    /// Layout is 4/5 width for the tab's own content and 1/5 for the summary column
    /// (unit SN, Hourly/Daily, I/O Summary, Tossing Summary, Export), which every tab
    /// but Health keeps - S2.4.4.4 draws Health across the full width.
    /// </para>
    /// <para>
    /// I/O, Tossing and Yield are three projections of one query: switching between
    /// them re-projects the same records rather than re-querying, so they can never
    /// show different windows. Glue reads the weighings in the same window. Health is
    /// the cartridges and nozzles as they are now, so it has no time filter.
    /// </para>
    /// <para>
    /// The page is split over four files: this one (tabs, filter, loading, export),
    /// and one each for the production tabs, Glue and Health.
    /// </para>
    /// </remarks>
    public partial class AudioDataViewModel : AudioPageViewModel
    {
        public override AppPageNames PageName => AppPageNames.Data;

        private const string IoTab = "I/O";
        private const string TossingTab = "Tossing";
        private const string YieldTab = "Yield";
        private const string HealthTab = "Health";
        private const string GlueTab = "Glue";

        private readonly IProductionRepository _production;
        private readonly IDispenserDataService _dispenser;
        private readonly ICsvExportService _csv;
        private readonly IDialogService _dialog;
        private readonly IAccessLog _accessLog;
        private readonly IAppSession _session;


        /// <summary>
        /// The yield target drawn on the Yield tab, in percent. Set by the host from
        /// App.config (TargetYieldPercent) before the page is built; null draws no line.
        /// </summary>
        public static double? TargetYieldPercent { get; set; }


        // ==================================================================
        // Tabs:  I/O | Tossing | Yield | Health | Glue | Axis  (S2.4.4 Data page X4,
        //        S2.4.4.6 Coordinate Data Analysis)
        // ==================================================================

        public ObservableCollection<string> Views { get; } =
            new ObservableCollection<string> { IoTab, TossingTab, YieldTab, HealthTab, GlueTab, AxisTab };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsIoView), nameof(IsTossingView), nameof(IsYieldView),
            nameof(IsHealthView), nameof(IsGlueView), nameof(IsAxisView), nameof(IsProductionView), nameof(IsTimeFiltered),
            nameof(HasBuckets), nameof(HasSummaryColumn), nameof(QueryButtonText), nameof(StatusText))]
        private string _selectedView = IoTab;

        public bool IsIoView => SelectedView == IoTab;

        public bool IsTossingView => SelectedView == TossingTab;

        public bool IsYieldView => SelectedView == YieldTab;

        public bool IsHealthView => SelectedView == HealthTab;

        public bool IsGlueView => SelectedView == GlueTab;

        /// <summary>I/O, Tossing and Yield: the tabs built from unit records, with the unit table and Hourly/Daily.</summary>
        public bool IsProductionView => IsIoView || IsTossingView || IsYieldView;

        /// <summary>Health shows the machine as it is now, so it has no From/To.</summary>
        public bool IsTimeFiltered => !IsHealthView;

        /// <summary>Hourly | Daily: the production tabs, and Glue's trend (S2.4.4.5 slides 3 and 4).</summary>
        public bool HasBuckets => IsProductionView || IsGlueView;

        /// <summary>
        /// The 1/5 summary column (unit SN, I/O and Tossing Summary, Export). S2.4.4.4
        /// draws Health across the full width without it, so its Export moves up to the
        /// filter row.
        /// </summary>
        public bool HasSummaryColumn => !IsHealthView && !IsAxisView;

        public string QueryButtonText => IsHealthView ? "Refresh" : "Query";

        partial void OnSelectedViewChanged(string value)
        {
            if (IsProductionView)
                ProjectProduction();

            ExportCommand.NotifyCanExecuteChanged();
        }


        // ==================================================================
        // Hourly | Daily
        // ==================================================================

        public ObservableCollection<string> Buckets { get; } =
            new ObservableCollection<string> { "Hourly", "Daily" };

        [ObservableProperty]
        private string _selectedBucket = "Hourly";

        public bool IsDaily => SelectedBucket == "Daily";

        partial void OnSelectedBucketChanged(string value)
        {
            OnPropertyChanged(nameof(IsDaily));

            // Moving the window is the point of the switch, so the range follows it and
            // the data is re-read.
            SetDefaultWindow();

            if (RefreshCommand.CanExecute(null))
                RefreshCommand.Execute(null);
        }

        /// <summary>
        /// Hourly shows the latest 12 hours, as S2.4.4.3 asks ("The latest 12-hour data
        /// is displayed by default") and slide 59's twelve hourly columns show. Daily
        /// shows the latest seven days.
        /// </summary>
        private void SetDefaultWindow()
        {
            DateTime now = DateTime.Now;
            EndTime = now;
            StartTime = IsDaily
                ? now.Date.AddDays(-6)
                : new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(-11);
        }


        // ==================================================================
        // Time filter and status
        // ==================================================================

        [ObservableProperty]
        private DateTime _startTime;

        [ObservableProperty]
        private DateTime _endTime;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        private string _productionStatus = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        private string _glueStatus = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        private string _healthStatus = string.Empty;

        /// <summary>The status line for the tab in front.</summary>
        public string StatusText =>
            IsHealthView ? HealthStatus : IsGlueView ? GlueStatus : IsAxisView ? AxisStatus : ProductionStatus;



        // ==================================================================
        // Query and Export
        // ==================================================================

        public IAsyncRelayCommand RefreshCommand { get; }

        public IRelayCommand ExportCommand { get; }

        partial void OnIsBusyChanged(bool value)
        {
            RefreshCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
        }

        private async Task RefreshAsync()
        {
            if (EndTime < StartTime && IsTimeFiltered)
            {
                ProductionStatus = GlueStatus = "End time is before start time.";
                return;
            }

            IsBusy = true;
            try
            {
                // Every tab is loaded, so switching tabs never shows a stale window.
                await LoadProductionAsync();
                await LoadGlueAsync();
                await LoadHealthAsync();
                await LoadAxisAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanExport()
        {
            if (IsBusy)
                return false;

            if (IsGlueView)
                return _glueRecords.Count > 0;

            if (IsHealthView)
                return GlueCartridges.Count > 0 || Nozzles.Count > 0;

            if (IsAxisView)
                return _axisRecords.Count > 0;

            return _records.Count > 0;
        }

        /// <summary>Slide 59's Export: writes the tab in front, all of it, not just the rows on screen.</summary>
        private void Export()
        {
            if (_csv == null)
            {
                _dialog?.ShowWarning("CSV export is not available on this machine.", "Export");
                return;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            if (IsGlueView)
                _csv.Export(GlueExportHeaders(), GlueExportRows(), "GlueWeight_" + stamp + ".csv");
            else if (IsHealthView)
                _csv.Export(HealthExportHeaders(), HealthExportRows(), "GlueHealth_" + stamp + ".csv");
            else if (IsAxisView)
                _csv.Export(AxisExportHeaders(), AxisExportRows(), "Coordinates_" + stamp + ".csv");
            else
                _csv.Export(UnitExportHeaders(), UnitExportRows(), SelectedView.Replace("/", "") + "_" + stamp + ".csv");
        }


        // ==================================================================
        // Access
        // ==================================================================

        /// <summary>
        /// The Glue and Health settings are OSS settings (S2.4.4.4: "OSS may configure"),
        /// so Level 2 and up may change them.
        /// </summary>
        public bool CanEditSettings => CanEdit && _dispenser != null;

        protected override void OnAccessLevelChanged(Nova.Abstractions.Data.AccessLevel level)
        {
            OnPropertyChanged(nameof(CanEditSettings));

            // Signing out, or down to Level 1, mid-edit drops the unsaved changes.
            if (!CanEditSettings)
            {
                if (IsEditingWeighing)
                    CancelWeighingSettings();

                if (IsEditingGlueControl)
                    CancelGlueControl();
            }

            EditWeighingSettingsCommand.NotifyCanExecuteChanged();
            EditGlueControlCommand.NotifyCanExecuteChanged();

            // A sign-in or sign-out starts the page from its defaults too (RPT_PS_05).
            ResetWindowAndRefresh();
        }


        // ==================================================================
        // Construction
        // ==================================================================

        public AudioDataViewModel(
            IAppSession session,
            IProductionRepository production = null,
            ICsvExportService csv = null,
            IDialogService dialog = null,
            IDispenserDataService dispenser = null,
            IAccessLog accessLog = null,
            ICoordinateDataSource coordinates = null)
            : base(session)
        {
            _session = session;
            _production = production;
            _dispenser = dispenser;
            _coordinates = coordinates;
            _csv = csv;
            _dialog = dialog;
            _accessLog = accessLog;

            RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
            ExportCommand = new RelayCommand(Export, CanExport);
            EditWeighingSettingsCommand = new RelayCommand(EditWeighingSettings, CanEditWeighing);
            EditGlueControlCommand = new RelayCommand(EditGlueControl, CanEditGlueControl);

            SetDefaultWindow();

            // Build the empty charts up front so the page renders empty charts rather
            // than blank boxes before the first query returns.
            ProjectProduction();
            BuildGlue();
            BuildHealth();
        }

        /// <summary>
        /// FAM 2 RPT_PS_05: the page opens on its default window every time, rather than on
        /// whatever range the last operator left (the view model lives for the whole HMI).
        /// </summary>
        public override void OnNavigatedTo()
        {
            base.OnNavigatedTo();
            ResetWindowAndRefresh();
        }

        private void ResetWindowAndRefresh()
        {
            SetDefaultWindow();

            if (RefreshCommand != null && RefreshCommand.CanExecute(null))
                RefreshCommand.Execute(null);
        }

        private static IReadOnlyList<T> Empty<T>()
        {
            return new T[0];
        }

        /// <summary>Slide 44: "All L1/L2/L3 actions are recorded in machine log".</summary>
        private void LogAction(string action)
        {
            if (_accessLog == null || _session == null)
                return;

            _accessLog.Action(_session.UserName, (int)AccessLevel, action);
        }
    }
}
