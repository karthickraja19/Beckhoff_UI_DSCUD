using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data;
using Nova.Abstractions.Data.Repositories;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The Home page - Audio SW DFM v5.4 slides 43 ("Home Page Formatting"), 54 ("Home
    /// Page"), 55 ("Sensor Dashboard"). The Vendor window (slide 57) is not built:
    /// the manual-operation GUI, the Homing button and the running log are on the Setting
    /// page's Manual tab.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Layout the slide dimensions: a 2/3-width left column split half-and-half into
    /// State Changes over Error Statistics, and a 1/3-width right column split into
    /// thirds - I/O Summary, Parameter Dashboard, station card.
    /// </para>
    /// <para>
    /// Where the data comes from: State Changes from the run / idle / down time in the
    /// database ([HealthData]), Error
    /// Statistics from the alarm log, the I/O summary and last unit from the production
    /// record, the Parameter Dashboard from the Config page's production spec limits and
    /// the live parameter values, the station card from the station profile, the links
    /// from each service's link status, and the sensor dashboard from the sensor
    /// service. A source that is missing leaves its panel saying so.
    /// </para>
    /// <para>
    /// The page is split over three files: this one (tabs, station card, refresh loop),
    /// the dashboard panels, and the links and sensor dashboard.
    /// </para>
    /// </remarks>
    public partial class AudioHomeViewModel : AudioPageViewModel
    {
        public override AppPageNames PageName => AppPageNames.Home;

        private static readonly TimeSpan FastRefresh = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ChartRefresh = TimeSpan.FromMinutes(5);

        private readonly IAppSession _session;
        private readonly IStationProfileService _profile;
        private readonly IConnectionStatusService _connections;
        private readonly ILogFolderService _logFolders;
        private readonly IDialogService _dialog;
        private readonly INavigationService _navigation;
        private readonly IProductionRepository _production;
        private readonly IAlarmLogRepository _alarms;
        private readonly IHiveStateHistory _stateHistory;
        private readonly IMachineSettingsService _settings;
        private readonly IParameterValueSource _parameterValues;
        private readonly IDispenserDataService _dispenser;
        private readonly IConsumableStore _consumables;
        private readonly IPictureQuantityCheck _pictures;
        private readonly ISensorService _sensors;
        private readonly IAccessLog _accessLog;
        private readonly IReadOnlyList<IServiceLinkStatus> _linkSources;

        private CancellationTokenSource _refreshLoop;


        // ==================================================================
        // Station card  (slide 43)
        // ==================================================================

        /// <summary>"STN01  #1" - station name and QPL number.</summary>
        [ObservableProperty]
        private string _stationTitle = "—";

        [ObservableProperty]
        private string _site = "—";

        [ObservableProperty]
        private string _vendor = "—";

        [ObservableProperty]
        private string _softwareVersion = "—";

        [ObservableProperty]
        private string _mainSoftwarePath = "—";

        /// <summary>
        /// Slide 43: "Refresh Hash key each time machine state changes". Recomputed on
        /// every status change the orchestrator raises; the service caches on the file
        /// stamp so an unchanged exe is not re-hashed.
        /// </summary>
        [ObservableProperty]
        private string _mainSoftwareHash = "—";

        /// <summary>Slide 40: the station card goes orange under remote qualification.</summary>
        [ObservableProperty]
        private bool _isRemoteQualification;


        // ==================================================================
        // Commands
        // ==================================================================

        /// <summary>
        /// Slide 43: "Click Machine log and HIVE log to open the path of the log files".
        /// The machine log is the folder the running log is written to (App.config
        /// LogFilePath), falling back to the profile's path when that is not set.
        /// </summary>
        [RelayCommand]
        private void OpenMachineLog()
        {
            string path = _logFolders != null && !string.IsNullOrWhiteSpace(_logFolders.LogFolderPath)
                ? _logFolders.LogFolderPath
                : (_profile != null ? _profile.Current.MachineLogPath : null);

            OpenFolder(path, "Machine log");
        }

        [RelayCommand]
        private void OpenHiveLog()
        {
            OpenFolder(_profile != null ? _profile.Current.HiveLogPath : null, "HIVE log");
        }

        /// <summary>
        /// Slide 43: "Main SW path to show the path of the main SW, click to open the
        /// path" - the folder the running exe is in.
        /// </summary>
        [RelayCommand]
        private void OpenMainSoftwarePath()
        {
            string exe = _profile != null ? _profile.Current.MainSoftwarePath : null;
            OpenFolder(string.IsNullOrWhiteSpace(exe) ? null : System.IO.Path.GetDirectoryName(exe), "Main SW");
        }

        /// <summary>Opens the folder, or says why not.</summary>
        private void OpenFolder(string path, string what)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _dialog?.ShowWarning(what + " path is not configured.", what);
                return;
            }

            if (_logFolders != null && _logFolders.OpenFolder(path))
                return;

            _dialog?.ShowWarning(what + " folder does not exist yet:" + Environment.NewLine + path, what);
        }

        /// <summary>
        /// Slide 54: clicking the I/O summary jumps to the Data tab, clicking the error
        /// panel jumps to the Alarm tab.
        /// </summary>
        [RelayCommand]
        private void ShowDataPage()
        {
            _navigation?.NavigateTo(AppPageNames.Data);
        }

        [RelayCommand]
        private void ShowAlarmPage()
        {
            _navigation?.NavigateTo(AppPageNames.Alarm);
        }


        // ==================================================================

        public AudioHomeViewModel(
            IAppSession session,
            IMachineOrchestrator orchestrator = null,
            IStationProfileService profile = null,
            IConnectionStatusService connections = null,
            ILogFolderService logFolders = null,
            IDialogService dialog = null,
            INavigationService navigation = null,
            IProductionRepository production = null,
            IAlarmLogRepository alarms = null,
            IHiveStateHistory stateHistory = null,
            IMachineSettingsService settings = null,
            IParameterValueSource parameterValues = null,
            IDispenserDataService dispenser = null,
            IEnumerable<IServiceLinkStatus> links = null,
            ISensorService sensors = null,
            IAccessLog accessLog = null,
            IErrorCodeCatalog errorCodes = null,
            // Parameter Dashboard: the nozzle health of each gantry (S3.3) and the last
            // unit's picture count (T5).
            IConsumableStore consumables = null,
            IPictureQuantityCheck pictures = null)
            : base(session, AccessBypass.ConfigAndSettings)
        {
            _consumables = consumables;
            _pictures = pictures;
            _session = session;
            _profile = profile;
            _connections = connections;
            _logFolders = logFolders;
            _dialog = dialog;
            _navigation = navigation;
            _production = production;
            _alarms = alarms;
            _stateHistory = stateHistory;
            _settings = settings;
            _parameterValues = parameterValues;
            _dispenser = dispenser;
            _sensors = sensors;
            _accessLog = accessLog;
            _errorCodes = errorCodes;
            _linkSources = links != null ? links.ToList() : new List<IServiceLinkStatus>();

            InitialiseDashboard();
            ApplyStationCard();

            // These collections are built once, on the UI thread, before anything
            // subscribes. The background-thread handlers then only move values inside
            // the rows - see the note on SummaryRow.
            BuildLinkRows();
            BuildSensorRows();
            ApplyConnections();

            if (_connections != null)
                _connections.StatusChanged += OnConnectionsChanged;

            if (_profile != null)
                _profile.ProfileChanged += OnProfileChanged;

            // Slide 43 ties the hash refresh to machine state changes.
            if (orchestrator != null)
                orchestrator.OnMachineStatusChanged += OnMachineStatusChanged;
        }


        // ==================================================================
        // Refresh loop
        // ==================================================================

        /// <summary>
        /// While the page is on screen: the last unit and the parameter values every ten
        /// seconds, the two charts every five minutes, and the I/O statistics on the
        /// hour (slide 43: "I/O Summary statistics update hourly").
        /// </summary>
        public override void OnNavigatedTo()
        {
            base.OnNavigatedTo();

            if (_refreshLoop != null)
                _refreshLoop.Cancel();

            _refreshLoop = new CancellationTokenSource();
            var ignored = RunRefreshLoopAsync(_refreshLoop.Token);
        }

        public override void OnNavigatedFrom()
        {
            if (_refreshLoop != null)
            {
                _refreshLoop.Cancel();
                _refreshLoop = null;
            }

            base.OnNavigatedFrom();
        }

        /// <remarks>
        /// Started from the UI thread, so every await comes back to it and the bound
        /// collections are only ever changed there.
        /// </remarks>
        private async Task RunRefreshLoopAsync(CancellationToken token)
        {
            try
            {
                await RefreshAllAsync();

                DateTime nextCharts = DateTime.Now + ChartRefresh;
                DateTime nextStats = NextHour(DateTime.Now);

                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(FastRefresh, token);

                    await RefreshLastUnitAsync();
                    await RefreshParametersAsync();

                    DateTime now = DateTime.Now;
                    if (now >= nextStats)
                    {
                        await RefreshIoSummaryAsync();
                        nextStats = NextHour(now);
                    }

                    // The State Changes chart follows the machine, so it is redrawn on every
                    // pass (its open segment is read from memory, the rest is one small query).
                    await RefreshStateChartAsync();

                    if (now >= nextCharts)
                    {
                        await RefreshErrorStatisticsAsync(followNow: true);
                        nextCharts = now + ChartRefresh;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Left the page.
            }
        }

        private async Task RefreshAllAsync()
        {
            await RefreshStateChartAsync();
            await RefreshErrorStatisticsAsync(followNow: true);
            await RefreshIoSummaryAsync();
            await RefreshLastUnitAsync();
            await RefreshParametersAsync();
        }

        private static DateTime NextHour(DateTime time)
        {
            return new DateTime(time.Year, time.Month, time.Day, time.Hour, 0, 0).AddHours(1);
        }


        // ==================================================================
        // Station card
        // ==================================================================

        private void OnProfileChanged(object sender, EventArgs e) => ApplyStationCard();

        private void ApplyStationCard()
        {
            StationProfile p = _profile != null ? _profile.Current : null;
            if (p == null)
                return;

            StationTitle = p.Station + "  " + p.QplNumber;
            Site = p.Site;
            Vendor = p.Vendor;
            SoftwareVersion = p.SoftwareVersion;
            MainSoftwarePath = p.MainSoftwarePath;
            IsRemoteQualification = p.RemoteQualification;
            MainSoftwareHash = _profile.ComputeMainSoftwareHash();
        }

        private void OnMachineStatusChanged(Nova.Abstractions.Machine.Enums.MachineStatus status)
        {
            if (_profile != null)
                MainSoftwareHash = _profile.ComputeMainSoftwareHash();
        }

        /// <summary>Slide 44: "All L1/L2/L3 actions are recorded in machine log".</summary>
        private void LogAction(string action)
        {
            if (_accessLog != null && _session != null)
                _accessLog.Action(_session.UserName, (int)AccessLevel, action);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_refreshLoop != null)
                    _refreshLoop.Cancel();

                if (_connections != null)
                    _connections.StatusChanged -= OnConnectionsChanged;

                if (_profile != null)
                    _profile.ProfileChanged -= OnProfileChanged;

                foreach (IServiceLinkStatus link in _linkSources)
                    link.Changed -= OnLinkChanged;

                if (_sensors != null)
                    _sensors.Changed -= OnSensorsChanged;
            }

            base.Dispose(disposing);
        }
    }
}
