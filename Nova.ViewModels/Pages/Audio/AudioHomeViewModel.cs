using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Nova.Abstractions.Data;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Machine.Models;
using Nova.Abstractions.Services;
using Nova.ViewModels.Audio;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The Home page - Audio SW DFM v5.4 slides 43 ("Home Page Formatting") and 54
    /// ("Home Page").
    /// </summary>
    /// <remarks>
    /// <para>
    /// Layout the slide dimensions: a 2/3-width left column split half-and-half into
    /// State Changes over Error Statistics, and a 1/3-width right column split into
    /// thirds - I/O Summary, Parameter Dashboard, station card.
    /// </para>
    /// <para>
    /// Where the data comes from: the I/O summary and the connection links are live
    /// (telemetry and the connection-status service), the station card is live (station
    /// profile plus a SHA-1 of the running exe), and the two charts plus the parameter
    /// dashboard are seeded with <see cref="SeedDemoData"/> because nothing in this
    /// build records seven days of HIVE state, per-category error counts or the AME
    /// critical-parameter list yet. <see cref="IsSampleData"/> is true whenever a panel
    /// is showing seeded figures, and the view puts a "sample data" marker on it - an
    /// operator must never be unable to tell a real yield from a placeholder.
    /// </para>
    /// </remarks>
    public partial class AudioHomeViewModel : AudioPageViewModel
    {
        public override AppPageNames PageName => AppPageNames.Home;

        private readonly IMachineTelemetryService _telemetry;
        private readonly IStationProfileService _profile;
        private readonly IConnectionStatusService _connections;
        private readonly ILogFolderService _logFolders;
        private readonly IDialogService _dialog;
        private readonly INavigationService _navigation;

        /// <summary>
        /// True while any panel on this page is showing seeded rather than recorded
        /// figures. Surfaced in the UI, not just logged.
        /// </summary>
        [ObservableProperty]
        private bool _isSampleData = true;


        // ==================================================================
        // Main | Vendor  (slide 54 / 57)
        // ==================================================================

        /// <summary>
        /// Slide 57: the Vendor tab is "for semi-auto stations only (require OP
        /// loading/scanning)". Both segments always exist so the control does not
        /// appear and disappear; the Vendor pane says what it is for when the station
        /// is not semi-auto.
        /// </summary>
        public ObservableCollection<string> Tabs { get; } =
            new ObservableCollection<string> { "Main", "Vendor" };

        [ObservableProperty]
        private string _selectedTab = "Main";

        public bool IsMainTab => SelectedTab == "Main";

        partial void OnSelectedTabChanged(string value)
        {
            OnPropertyChanged(nameof(IsMainTab));

            // The manual GUI is a DI singleton, so a grid left unlocked on it stays
            // unlocked. Leaving the Vendor pane ends any edit standing on it.
            if (value != "Vendor" && Manual != null)
                Manual.OnNavigatedFrom();
        }

        /// <summary>
        /// The manual-operation GUI shown in the Vendor pane. Slide 57 gives the Vendor
        /// tab's content as exactly that, for semi-auto stations that need an operator
        /// to load or scan. Null on a build with no manual screens, which leaves the
        /// pane showing its "semi-auto only" explanation.
        /// </summary>
        public PageViewModel Manual { get; }

        /// <summary>Whether the Vendor pane has a manual GUI to show.</summary>
        public bool HasManual => Manual != null;

        /// <summary>
        /// Live running log beside the manual GUI, so an operator driving the station by
        /// hand can see what the machine just did. Not a DFM requirement - slide 57
        /// leaves the pane's content open - but the Machine Log button on the Main tab
        /// only opens a folder of files after the fact.
        /// </summary>
        public RunningLogViewModel RunningLog { get; }


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


        // ==================================================================
        // Error Statistics  (slide 43: "shows today's data. Past 7 days' data is
        // stored locally. Filter slider can be used to view locally stored data")
        // ==================================================================

        public ObservableCollection<ErrorStatRow> ErrorStatistics { get; } =
            new ObservableCollection<ErrorStatRow>();

        /// <summary>Left end of the filter slider, as the slide's two timestamp boxes.</summary>
        [ObservableProperty]
        private DateTime _errorFilterStart = DateTime.Today.AddDays(-6);

        [ObservableProperty]
        private DateTime _errorFilterEnd = DateTime.Now;


        // ==================================================================
        // I/O Summary  (slide 43: "statistics update hourly. Each unit status shows as
        // it completes processing. Failed units turn SN background red")
        // ==================================================================

        /// <summary>Serial number of the unit that most recently finished.</summary>
        [ObservableProperty]
        private string _lastUnitSn = "—";

        /// <summary>
        /// Whether that unit passed. Drives the banner fill: DFM green #00F900 on a
        /// pass, red #EC5D57 on a fail.
        /// </summary>
        [ObservableProperty]
        private bool _lastUnitPassed = true;

        public ObservableCollection<SummaryRow> IoSummary { get; } =
            new ObservableCollection<SummaryRow>();


        // ==================================================================
        // Parameter Dashboard  (slide 43)
        // ==================================================================

        public ObservableCollection<ParameterRow> Parameters { get; } =
            new ObservableCollection<ParameterRow>();


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

        /// <summary>
        /// Slide 40 gives the station card an orange background while the machine is
        /// under remote qualification.
        /// </summary>
        [ObservableProperty]
        private bool _isRemoteQualification;

        public ObservableCollection<LinkStatusRow> Links { get; } =
            new ObservableCollection<LinkStatusRow>();

        /// <summary>
        /// Slide 54: "Show sensor connection button after login". Expands the sensor
        /// dashboard from slide 55 inline under the card.
        /// </summary>
        [ObservableProperty]
        private bool _isSensorPanelExpanded;

        public ObservableCollection<LinkStatusRow> Sensors { get; } =
            new ObservableCollection<LinkStatusRow>();


        // ==================================================================
        // Commands
        // ==================================================================

        /// <summary>
        /// Slide 43: "Click Machine log and HIVE log to open the path of the log files".
        /// </summary>
        /// <remarks>
        /// <see cref="ILogFolderService"/> opens one fixed configured folder and takes
        /// no path, so it can only serve the machine log. The HIVE log and the main-SW
        /// folder are separate paths from the station profile and have nowhere to go
        /// yet, so those two commands report the path instead of opening it. Widening
        /// the service to take a path is a change to a contract the legacy Log nav
        /// entry also uses, which is not this pass's to make.
        /// </remarks>
        [RelayCommand]
        private void OpenMachineLog()
        {
            if (_logFolders != null && _logFolders.FolderExists)
            {
                _logFolders.OpenLogFolder();
                return;
            }

            ReportPath(
                _logFolders != null && !string.IsNullOrWhiteSpace(_logFolders.LogFolderPath)
                    ? _logFolders.LogFolderPath
                    : (_profile != null ? _profile.Current.MachineLogPath : null),
                "Machine log");
        }

        [RelayCommand]
        private void OpenHiveLog()
        {
            ReportPath(_profile != null ? _profile.Current.HiveLogPath : null, "HIVE log");
        }

        /// <summary>Slide 43: "Main SW path to show the path of the main SW, click to open the path".</summary>
        [RelayCommand]
        private void OpenMainSoftwarePath()
        {
            ReportPath(_profile != null ? _profile.Current.MainSoftwarePath : null, "Main SW");
        }

        private void ReportPath(string path, string what)
        {
            if (_dialog == null)
                return;

            if (string.IsNullOrWhiteSpace(path))
            {
                _dialog.ShowWarning(what + " path is not configured.", what);
                return;
            }

            _dialog.ShowInfo(path, what);
        }

        /// <summary>
        /// Slide 54: clicking the I/O summary jumps to the Data tab, clicking the error
        /// panel jumps to the Alarm tab. Whole panels are the targets, which is why the
        /// view wraps them in card buttons rather than adding a link.
        /// </summary>
        [RelayCommand]
        private void ShowDataPage()
        {
            if (_navigation != null)
                _navigation.NavigateTo(AppPageNames.Data);
        }

        [RelayCommand]
        private void ShowAlarmPage()
        {
            if (_navigation != null)
                _navigation.NavigateTo(AppPageNames.Alarm);
        }

        [RelayCommand]
        private void ToggleSensorPanel()
        {
            IsSensorPanelExpanded = !IsSensorPanelExpanded;
        }


        // ==================================================================

        public AudioHomeViewModel(
            IAppSession session,
            IMachineTelemetryService telemetry,
            IMachineOrchestrator orchestrator = null,
            IStationProfileService profile = null,
            IConnectionStatusService connections = null,
            ILogFolderService logFolders = null,
            IDialogService dialog = null,
            INavigationService navigation = null,
            // Slide 57: the Vendor pane's content is the manual-operation GUI.
            PageViewModel manual = null,
            IRunningLogService runningLog = null)
            : base(session)
        {
            _telemetry = telemetry;
            _profile = profile;
            _connections = connections;
            _logFolders = logFolders;
            _dialog = dialog;
            _navigation = navigation;

            Manual = manual;
            RunningLog = new RunningLogViewModel(runningLog);

            BuildStateChangeChart();
            SeedDemoData();
            ApplyStationCard();

            // Both collections are built once, on the UI thread, before anything
            // subscribes. The background-thread handlers below then only move values
            // inside these rows - see the note on SummaryRow.
            BuildIoSummaryRows();
            BuildLinkRows();
            ApplyTelemetry(telemetry != null ? telemetry.Latest : null);
            ApplyConnections();

            if (_telemetry != null)
                _telemetry.TelemetryUpdated += OnTelemetryUpdated;

            if (_connections != null)
                _connections.StatusChanged += OnConnectionsChanged;

            if (_profile != null)
                _profile.ProfileChanged += OnProfileChanged;

            // Slide 43 ties the hash refresh to machine state changes.
            if (orchestrator != null)
                orchestrator.OnMachineStatusChanged += OnMachineStatusChanged;
        }

        /// <summary>
        /// Seven stacked columns, T-6 to T, each split into the three HIVE states in
        /// the DFM's own colours.
        /// </summary>
        /// <remarks>
        /// Percentage-stacked, because the slide's Y axis is 0-100% rather than hours.
        /// The figures are seeded - see <see cref="IsSampleData"/>.
        /// </remarks>
        private void BuildStateChangeChart()
        {
            // Deterministic, not random: a seeded chart that reshuffles on every
            // navigation looks like live data and is the exact confusion this page has
            // to avoid.
            double[] running = { 90, 30, 10, 70, 8, 70, 60 };
            double[] idle = { 4, 60, 80, 27, 89, 22, 15 };
            double[] downtime = { 6, 10, 10, 3, 3, 8, 25 };

            StateChangeSeries = new ISeries[]
            {
                new StackedColumnSeries<double>
                {
                    Name = "Running",
                    Values = running,
                    Fill = AudioChartPalette.Fill(AudioChartPalette.Running),
                    Stroke = null,
                    IgnoresBarPosition = true
                },
                new StackedColumnSeries<double>
                {
                    Name = "Idle",
                    Values = idle,
                    Fill = AudioChartPalette.Fill(AudioChartPalette.Idle),
                    Stroke = null,
                    IgnoresBarPosition = true
                },
                new StackedColumnSeries<double>
                {
                    Name = "Downtime",
                    Values = downtime,
                    Fill = AudioChartPalette.Fill(AudioChartPalette.Downtime),
                    Stroke = null,
                    IgnoresBarPosition = true
                }
            };

            // Slide 43 labels the newest column "T (YYMMDD)" and the rest T-1 .. T-6.
            var labels = new List<string>();
            for (int offset = 6; offset >= 1; offset--)
                labels.Add("T-" + offset);
            labels.Add("T (" + DateTime.Today.ToString("yyMMdd") + ")");

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

        /// <summary>
        /// Fills the panels no service feeds yet, using the DFM's own example figures so
        /// the layout can be checked against the slide. Paired with
        /// <see cref="IsSampleData"/>, which the view displays.
        /// </summary>
        private void SeedDemoData()
        {
            // Error categories and counts are the ones drawn on slide 43.
            var counts = new[]
            {
                new KeyValuePair<string, int>("MES Error", 1928),
                new KeyValuePair<string, int>("Safety Error", 1831),
                new KeyValuePair<string, int>("Scanning Error", 1657),
                new KeyValuePair<string, int>("Vision Error", 1505),
                new KeyValuePair<string, int>("Cylinder Error", 1466),
                new KeyValuePair<string, int>("Motion Error", 1215),
                new KeyValuePair<string, int>("Sensor Error", 882),
                new KeyValuePair<string, int>("PDI Error", 524)
            };

            int max = counts.Max(c => c.Value);
            ErrorStatistics.Clear();
            foreach (var entry in counts)
                ErrorStatistics.Add(new ErrorStatRow(entry.Key, entry.Value, max));

            // Parameter dashboard. Slide 43 requires out-of-spec rows to sort to the
            // top, so the seed deliberately includes two and the sort below proves it.
            var parameters = new List<ParameterRow>
            {
                new ParameterRow("Dispense Pressure", "42.8", 40, 45),
                new ParameterRow("Needle Height", "1.980", 1.9, 2.1),
                new ParameterRow("UV Cure Time", "13.4", 10, 12),
                new ParameterRow("Stage Temperature", "24.1", 22, 26),
                new ParameterRow("Vacuum Level", "-78", -85, -70),
                new ParameterRow("Glue Weight", "0.0412", 0.038, 0.044),
                new ParameterRow("Firing Pin Frequency", "310", 280, 300),
                // "Show current glue SN with no limits (dispenser only)" - a limitless,
                // non-numeric row, which must never be flagged out of spec.
                new ParameterRow("Glue SN", "PBA-123-112233", null, null)
            };

            Parameters.Clear();
            foreach (var row in parameters.OrderByDescending(p => p.IsOutOfSpec))
                Parameters.Add(row);

            LastUnitSn = "FVMDJ20X6G";
            LastUnitPassed = true;
        }

        private void OnTelemetryUpdated(object sender, MachineTelemetrySnapshot snapshot)
        {
            ApplyTelemetry(snapshot);
        }

        private const string NoValue = "—";

        private SummaryRow _inputOutput;
        private SummaryRow _yield;
        private SummaryRow _passFail;
        private SummaryRow _uph;
        private SummaryRow _cycleTime;

        /// <summary>
        /// The I/O Summary block from slide 43: Input/Output, Yield, Pass/Fail, UPH, CT.
        /// Built once; <see cref="ApplyTelemetry"/> only moves the values.
        /// </summary>
        private void BuildIoSummaryRows()
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
        }

        /// <summary>
        /// Updates the I/O summary in place. Safe to call from the telemetry thread:
        /// it only sets scalar properties on rows that already exist.
        /// </summary>
        private void ApplyTelemetry(MachineTelemetrySnapshot snapshot)
        {
            if (snapshot == null)
            {
                _inputOutput.Value = NoValue;
                _yield.Value = NoValue;
                _passFail.Value = NoValue;
                _uph.Value = NoValue;
                _cycleTime.Value = NoValue;
                return;
            }

            _inputOutput.Value = snapshot.OverallCount.ToString("N0") + "/" + snapshot.PassCount.ToString("N0");
            _yield.Value = snapshot.YieldPercent.ToString("0.0") + "%";
            _passFail.Value = snapshot.PassCount.ToString("N0") + "/" + snapshot.FailCount.ToString("N0");
            _uph.Value = snapshot.Uph.ToString("N0");
            _cycleTime.Value = snapshot.CycleTimeSeconds.ToString("0.00") + " s";
        }

        private void OnProfileChanged(object sender, EventArgs e) => ApplyStationCard();

        private void ApplyStationCard()
        {
            if (_profile == null)
                return;

            StationProfile p = _profile.Current;
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

        private void OnConnectionsChanged(object sender, EventArgs e) => ApplyConnections();

        private readonly Dictionary<string, LinkStatusRow> _sensorsByName =
            new Dictionary<string, LinkStatusRow>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The three service links on the card, plus the per-device rows behind the
        /// sensor-connection expander.
        /// </summary>
        /// <remarks>
        /// HIVE and PDCA have no adapter in this build, so they report Disconnected -
        /// which is the truth, and the same thing the DFM's own slide 43 mockup shows
        /// for PDCA. MES has none either; it is shown Disconnected rather than omitted
        /// so the card keeps the three-link layout from the slide.
        /// </remarks>
        private void BuildLinkRows()
        {
            Links.Add(new LinkStatusRow("HIVE", false));
            Links.Add(new LinkStatusRow("MES", false));
            Links.Add(new LinkStatusRow("PDCA", false));
        }

        /// <summary>
        /// Refreshes the sensor rows. <see cref="IConnectionStatusService.StatusChanged"/>
        /// is raised on the poll thread, so rows are added to the bound collection only
        /// on the first pass (which runs from the constructor, on the UI thread) and
        /// every later pass just flips <c>IsConnected</c> on the rows already there.
        /// A device that appears later is skipped rather than added, because adding it
        /// would be the cross-thread collection mutation this is avoiding - the device
        /// set is fixed once the service has started, so that case does not arise in
        /// practice.
        /// </summary>
        private void ApplyConnections()
        {
            if (_connections == null)
                return;

            IReadOnlyList<ConnectionStatusEntry> entries = _connections.Read();
            if (entries == null)
                return;

            foreach (ConnectionStatusEntry entry in entries)
            {
                string name = entry.DeviceType + " " + entry.Name;

                LinkStatusRow row;
                if (_sensorsByName.TryGetValue(name, out row))
                {
                    row.IsConnected = entry.IsConnected;
                    continue;
                }

                if (_sensorsInitialised)
                    continue;

                row = new LinkStatusRow(name, entry.IsConnected);
                _sensorsByName[name] = row;
                Sensors.Add(row);
            }

            _sensorsInitialised = true;
        }

        private bool _sensorsInitialised;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_telemetry != null)
                    _telemetry.TelemetryUpdated -= OnTelemetryUpdated;

                if (_connections != null)
                    _connections.StatusChanged -= OnConnectionsChanged;

                if (_profile != null)
                    _profile.ProfileChanged -= OnProfileChanged;

                if (RunningLog != null)
                    RunningLog.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
