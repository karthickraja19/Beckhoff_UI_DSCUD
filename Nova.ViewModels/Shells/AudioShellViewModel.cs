using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Nova.Abstractions.Data;
using Nova.Abstractions.Machine.Enums;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Shells
{
    /// <summary>
    /// The Audio/Home indicator bar and the page host beneath it - Audio SW DFM v5.4
    /// slide 42 ("Indicator Bar Formatting and Alignment") and slide 39 ("Indicator
    /// Legend").
    /// </summary>
    /// <remarks>
    /// <para>
    /// Layout, per slide 42: six menu buttons aligned left, station ID centred,
    /// machine controls then the access-level and HIVE chips then the Audio wordmark
    /// aligned right. Every button is the same size with a fixed gap, and the whole
    /// bar turns red while the machine is down.
    /// </para>
    /// <para>
    /// This is the application's only shell. The sidebar-and-toolbar arrangement it
    /// replaced has been removed - this HMI drives one Audio machine and its GUI is
    /// the one the DFM specifies.
    /// </para>
    /// <para>
    /// HIVE is display-only here. The chip reports <see cref="HiveState"/> and the
    /// level chip reports <see cref="AccessLevel"/>, but nothing in this class
    /// reports a <c>machine_state</c> payload, and clicking the HIVE chip does not
    /// open the state-change dialog from slide 49. That, the planned-downtime code
    /// list and the PDCA screens are the next slice of work, deliberately left out.
    /// </para>
    /// </remarks>
    public partial class AudioShellViewModel : ViewModelBase
    {
        private readonly INavigationService _navigation;
        private readonly IAppSession _session;
        private readonly IMachineOrchestrator _orchestrator;
        private readonly IStationProfileService _profile;
        private readonly IDialogService _dialog;
        private readonly IActiveAlarmService _activeAlarms;

        public AppTitleBarViewModel TitleBar { get; }

        /// <summary>The six menu buttons, in the order slide 42 draws them.</summary>
        public ObservableCollection<AudioNavItem> NavItems { get; } =
            new ObservableCollection<AudioNavItem>();

        [ObservableProperty]
        private INavigablePage _currentPage;


        // ==================================================================
        // Window title - DFM slide 43
        // ==================================================================

        /// <summary>
        /// The SW version string in the window title, formatted per slide 43:
        /// <c>AAA_X.X.X.X_YYMMDD_TYPE</c>, where AAA is the three-letter vendor
        /// abbreviation, the four X's are the main / vision / laser / robot SW
        /// versions, YYMMDD is the release date and TYPE is one of POR, BKP, RTF or
        /// VAL.
        /// </summary>
        /// <remarks>
        /// Read verbatim from the station profile - it is not assembled from parts
        /// here, because inventing a "0" for a vision or laser version the profile does
        /// not carry would have the title claim a build that was never read from
        /// anywhere. A profile that has not been stamped yet shows its own placeholder
        /// and is visibly a placeholder.
        /// </remarks>
        [ObservableProperty]
        private string _softwareVersionTitle = string.Empty;


        // ==================================================================
        // Station ID - DFM slide 42
        // ==================================================================

        /// <summary>
        /// Station identifier shown centred in the bar. Slide 42 shows it as a grey
        /// "STATION ID" watermark for a common product and as a coloured plate
        /// carrying the SKU name for a multi-SKU product.
        /// </summary>
        [ObservableProperty]
        private string _stationId = string.Empty;

        /// <summary>Product type from the Settings page, which colours the station ID.</summary>
        [ObservableProperty]
        private ProductSku _sku = ProductSku.Common;

        /// <summary>
        /// True when the product has a SKU and the station ID therefore shows on a
        /// coloured plate. False shows the #CCCCCC watermark instead.
        /// </summary>
        public bool HasSku => Sku != ProductSku.Common;

        partial void OnSkuChanged(ProductSku value)
        {
            OnPropertyChanged(nameof(HasSku));
        }


        // ==================================================================
        // Access level - DFM slides 39 and 44
        // ==================================================================

        [ObservableProperty]
        private AccessLevel _accessLevel = AccessLevel.None;

        /// <summary>
        /// Artwork for the level chip. The DFM ships the chip with its label already
        /// drawn in ("Level 1", "Level 2", "Level 3" and the no-login icon), so this
        /// is an asset name rather than a colour plus a string.
        /// </summary>
        public string AccessLevelAsset
        {
            get
            {
                switch (AccessLevel)
                {
                    case AccessLevel.Level1: return "Level1";
                    case AccessLevel.Level2: return "Level2";
                    case AccessLevel.Level3: return "Level3";
                    default: return "No_login";
                }
            }
        }

        /// <summary>
        /// Slide 44: the level chip is what opens the login dialog ("Click for pop up").
        /// </summary>
        public string AccessLevelTooltip =>
            AccessLevel == AccessLevel.None
                ? "Not signed in - click to sign in"
                : "Signed in as " + _session.UserName + " (" + AccessLevel + ") - click to switch user";

        partial void OnAccessLevelChanged(AccessLevel value)
        {
            OnPropertyChanged(nameof(AccessLevelAsset));
            OnPropertyChanged(nameof(AccessLevelTooltip));
            OnPropertyChanged(nameof(CanChangeHiveState));

            bool loggedIn = value != AccessLevel.None;
            foreach (AudioNavItem item in NavItems)
                item.IsLoggedIn = loggedIn;
        }


        // ==================================================================
        // HIVE status chip - DFM slides 39 and 42
        // ==================================================================

        [ObservableProperty]
        private HiveState _hiveState = HiveState.Disconnected;

        /// <summary>Artwork for the HIVE chip, again label-in-image.</summary>
        public string HiveStateAsset
        {
            get
            {
                switch (HiveState)
                {
                    case HiveState.Running: return "HIVE_Running";
                    case HiveState.Idle: return "HIVE_Idle";
                    case HiveState.Downtime: return "HIVE_DT";
                    case HiveState.PlannedDowntime: return "HIVE_Plan_DT";
                    case HiveState.Engineering: return "HIVE_Engineering";
                    default: return "HIVE_Disabled";
                }
            }
        }

        /// <summary>
        /// Slide 42: "Downtime state turns indicator bar background red". Bound by the
        /// view to swap the whole bar's fill to #EB7373.
        /// </summary>
        public bool IsDowntime =>
            HiveState == HiveState.Downtime ||
            HiveState == HiveState.PlannedDowntime ||
            HiveState == HiveState.Engineering;

        /// <summary>
        /// Slide 42: the chip is "read only for no login and L1"; slide 49 makes it
        /// the state-change control for "Level 2/3 OSS only - prohibited for Level 1".
        /// </summary>
        public bool CanChangeHiveState => AccessLevel >= AccessLevel.Level2;

        partial void OnHiveStateChanged(HiveState value)
        {
            OnPropertyChanged(nameof(HiveStateAsset));
            OnPropertyChanged(nameof(IsDowntime));
        }


        // ==================================================================
        // Machine controls - DFM slides 39, 47 and 48
        // ==================================================================

        [ObservableProperty]
        private MachineStatus _machineStatus;

        /// <summary>
        /// Which of the three machine buttons is lit. Slide 42: "Running/pause/stop
        /// buttons are colored when activated" - so exactly one is energized and the
        /// other two are de-energized, mirroring the machine rather than the last
        /// button pressed.
        /// </summary>
        public AudioIconState RunningIconState =>
            MachineStatus == MachineStatus.Running || MachineStatus == MachineStatus.Homing
                ? AudioIconState.Energized
                : AudioIconState.DeEnergized;

        public AudioIconState PausedIconState =>
            MachineStatus == MachineStatus.Pause
                ? AudioIconState.Energized
                : AudioIconState.DeEnergized;

        public AudioIconState StoppedIconState =>
            MachineStatus == MachineStatus.Stop ||
            MachineStatus == MachineStatus.Alarm ||
            MachineStatus == MachineStatus.Idle
                ? AudioIconState.Energized
                : AudioIconState.DeEnergized;

        /// <summary>
        /// Start, or resume when paused. The orchestrator's own Start decides which and
        /// raises its confirmation, as it does for the legacy shell.
        /// </summary>
        public IAsyncRelayCommand StartMachineCommand { get; }

        public IRelayCommand PauseMachineCommand { get; }

        public IRelayCommand StopMachineCommand { get; }

        // Slide 39 puts all three machine buttons at "No login: Enabled / L1, 2, 3:
        // Enabled", so access level does not gate them - only the machine's own state
        // does.
        private bool CanStart() =>
            MachineStatus == MachineStatus.Pause ||
            MachineStatus == MachineStatus.Stop ||
            MachineStatus == MachineStatus.Idle;

        private bool CanPause() =>
            MachineStatus != MachineStatus.Pause &&
            MachineStatus != MachineStatus.Stop &&
            MachineStatus != MachineStatus.Idle &&
            MachineStatus != MachineStatus.Homing &&
            MachineStatus != MachineStatus.Alarm;

        private bool CanStop() =>
            MachineStatus != MachineStatus.Stop &&
            MachineStatus != MachineStatus.Idle &&
            MachineStatus != MachineStatus.Alarm;

        // Not named OnMachineStatusChanged: [ObservableProperty] already generates a
        // partial hook by that name.
        private void HandleMachineStatusChanged(MachineStatus status)
        {
            MachineStatus = status;
        }

        partial void OnMachineStatusChanged(MachineStatus value)
        {
            OnPropertyChanged(nameof(RunningIconState));
            OnPropertyChanged(nameof(PausedIconState));
            OnPropertyChanged(nameof(StoppedIconState));

            StartMachineCommand.NotifyCanExecuteChanged();
            PauseMachineCommand.NotifyCanExecuteChanged();
            StopMachineCommand.NotifyCanExecuteChanged();

            // HIVE state follows the machine for display purposes only. The real
            // mapping has a 90-second idle timer and an error-code split (slides 7-9)
            // that belongs with the HIVE reporting work; until then the chip shows the
            // closest of the three states so the bar is not permanently grey.
            HiveState = MapToHiveState(value);
        }

        private static HiveState MapToHiveState(MachineStatus status)
        {
            switch (status)
            {
                case MachineStatus.Running:
                case MachineStatus.Homing:
                    return HiveState.Running;
                case MachineStatus.Alarm:
                    return HiveState.Downtime;
                case MachineStatus.Idle:
                case MachineStatus.Waiting:
                case MachineStatus.Pause:
                case MachineStatus.Stop:
                    return HiveState.Idle;
                default:
                    return HiveState.Disconnected;
            }
        }


        // ==================================================================
        // Commands
        // ==================================================================

        /// <summary>
        /// Level chip. Slide 44 makes this the login control ("Click for pop up").
        /// </summary>
        /// <remarks>
        /// Signed out, it asks the window for the login screen. Signed in, it signs the
        /// operator out - which under the DFM does not lock the GUI, it drops back to
        /// the no-login read-only state, so the bar stays up and Home, Alarm, Data and
        /// Vision stay reachable.
        /// <para>
        /// The DFM's own flow is a card swipe into a pop-up with two-step verification
        /// for Level 3 (slides 44-45). This build reuses the existing username/password
        /// login screen instead; card scanning and the second factor are not
        /// implemented.
        /// </para>
        /// </remarks>
        [RelayCommand]
        private void ChangeUser()
        {
            if (!_session.IsLoggedIn)
            {
                WeakReferenceMessenger.Default.Send(new ShowLoginMessage());
                return;
            }

            if (_dialog != null &&
                !_dialog.Confirm("Sign out and switch user?", "Switch User"))
            {
                return;
            }

            _session.Logout();
        }

        /// <summary>
        /// HIVE chip. Slide 49's state-change dialog is HIVE work and is out of scope
        /// for this pass, so the command says so plainly instead of silently doing
        /// nothing - an unexplained dead control on the indicator bar is worse than a
        /// message.
        /// </summary>
        [RelayCommand]
        private void ChangeHiveState()
        {
            if (_dialog == null)
                return;

            _dialog.ShowInfo(
                "Changing HIVE state from the GUI is not implemented yet. The chip shows "
                + "the current state only.",
                "HIVE");
        }

        [RelayCommand]
        private void Navigate(AppPageNames page)
        {
            _navigation.NavigateTo(page);
        }


        // ==================================================================

        public AudioShellViewModel(
            INavigationService navigation,
            IAppSession session,
            IMachineOrchestrator orchestrator,
            AppTitleBarViewModel titleBar,
            IStationProfileService profile = null,
            IDialogService dialog = null,
            IActiveAlarmService activeAlarms = null)
        {
            _navigation = navigation;
            _session = session;
            _orchestrator = orchestrator;
            _profile = profile;
            _dialog = dialog;
            _activeAlarms = activeAlarms;
            TitleBar = titleBar;

            // Slide 42, left to right. RequiresLogin is set for exactly the two the DFM
            // disables before login.
            NavItems.Add(new AudioNavItem(AppPageNames.Home, "Home", "Home", false));
            NavItems.Add(new AudioNavItem(AppPageNames.Alarm, "Alarm", "Alarm", false));
            NavItems.Add(new AudioNavItem(AppPageNames.Config, "Config", "Config", true));
            NavItems.Add(new AudioNavItem(AppPageNames.Data, "Data", "Data", false));
            NavItems.Add(new AudioNavItem(AppPageNames.Vision, "Vision", "Vision", false));
            NavItems.Add(new AudioNavItem(AppPageNames.Settings, "Setting", "Setting", true));

            ApplyProfile();
            if (_profile != null)
                _profile.ProfileChanged += OnProfileChanged;

            _machineStatus = orchestrator.CurrentStatus;
            _hiveState = MapToHiveState(_machineStatus);

            StartMachineCommand = new AsyncRelayCommand(orchestrator.StartAutoAsync, CanStart);
            PauseMachineCommand = new RelayCommand(orchestrator.Pause, CanPause);
            StopMachineCommand = new RelayCommand(orchestrator.Stop, CanStop);
            orchestrator.OnMachineStatusChanged += HandleMachineStatusChanged;

            _accessLevel = session != null && session.IsLoggedIn ? session.Level : AccessLevel.None;
            ApplyLoginToNavItems();
            session.PropertyChanged += OnSessionPropertyChanged;

            if (_activeAlarms != null)
            {
                _activeAlarms.ActiveAlarmsChanged += OnActiveAlarmsChanged;
                ApplyAlarmCount(_activeAlarms.GetActiveAlarms());
            }

            _currentPage = navigation.CurrentPage;
            navigation.Navigated += OnNavigated;
            SyncActiveNavItem(_currentPage);
        }

        /// <summary>
        /// The level the access check returned (DFM slide 52), read straight from the
        /// session rather than translated back out of a role string.
        /// </summary>
        private AccessLevel CurrentLevel()
        {
            return _session != null && _session.IsLoggedIn ? _session.Level : AccessLevel.None;
        }

        /// <summary>
        /// Pulls the station ID, SKU and window title out of the station profile.
        /// </summary>
        /// <remarks>
        /// Tolerates a null service so the indicator bar still renders in the XAML
        /// designer and in any test that constructs the shell without a profile - the
        /// fallbacks are the DFM's own placeholders ("STATION ID", no SKU), which is
        /// exactly what the slides draw for an unconfigured station.
        /// </remarks>
        private void ApplyProfile()
        {
            StationProfile profile = _profile != null ? _profile.Current : null;

            if (profile == null)
            {
                StationId = "STATION ID";
                Sku = ProductSku.Common;
                SoftwareVersionTitle = "Audio/Home";
                return;
            }

            StationId = string.IsNullOrWhiteSpace(profile.Station) ? "STATION ID" : profile.Station;
            Sku = profile.ProductType;
            SoftwareVersionTitle = string.IsNullOrWhiteSpace(profile.SoftwareVersion)
                ? "Audio/Home"
                : profile.SoftwareVersion;
        }

        private void OnProfileChanged(object sender, EventArgs e)
        {
            ApplyProfile();
        }

        private void OnSessionPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IAppSession.Level) ||
                e.PropertyName == nameof(IAppSession.IsLoggedIn) ||
                e.PropertyName == nameof(IAppSession.UserName))
            {
                AccessLevel = CurrentLevel();
                OnPropertyChanged(nameof(AccessLevelTooltip));
            }
        }

        private void ApplyLoginToNavItems()
        {
            bool loggedIn = _accessLevel != AccessLevel.None;
            foreach (AudioNavItem item in NavItems)
                item.IsLoggedIn = loggedIn;
        }

        private void OnActiveAlarmsChanged(object sender, IReadOnlyList<ActiveAlarmSnapshot> alarms)
        {
            ApplyAlarmCount(alarms);
        }

        /// <summary>
        /// Slide 39: the alarm bell "turns to Alarmed when an error occurs". Only the
        /// Alarm button is touched - the other five have no alerted artwork.
        /// </summary>
        private void ApplyAlarmCount(IReadOnlyList<ActiveAlarmSnapshot> alarms)
        {
            bool alerted = alarms != null && alarms.Count > 0;

            foreach (AudioNavItem item in NavItems)
            {
                if (item.Key == AppPageNames.Alarm)
                    item.IsAlerted = alerted;
            }
        }

        private void OnNavigated(INavigablePage page)
        {
            CurrentPage = page;
            SyncActiveNavItem(page);
        }

        private void SyncActiveNavItem(INavigablePage page)
        {
            if (page == null)
                return;

            foreach (AudioNavItem item in NavItems)
                item.IsActive = item.Key == page.PageName;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _navigation.Navigated -= OnNavigated;
                _session.PropertyChanged -= OnSessionPropertyChanged;
                _orchestrator.OnMachineStatusChanged -= HandleMachineStatusChanged;

                if (_activeAlarms != null)
                    _activeAlarms.ActiveAlarmsChanged -= OnActiveAlarmsChanged;

                if (_profile != null)
                    _profile.ProfileChanged -= OnProfileChanged;
            }

            base.Dispose(disposing);
        }
    }
}
