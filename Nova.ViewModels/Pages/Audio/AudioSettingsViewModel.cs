using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;
using ManualViewModel = Nova.ViewModels.Pages.Manual.ManualViewModel;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The Setting page - Audio SW DFM v5.4 slides 62 to 64.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A General block of six fields on the left over a Change Config button, and the
    /// machine's resolved hardware configuration on the right.
    /// </para>
    /// <para>
    /// Three rules from slide 62 drive the whole page:
    /// </para>
    /// <list type="bullet">
    /// <item><description>"Any change should be selected through the menu bar. Manually
    /// key-in is NOT allowed." Every field is therefore a closed list from
    /// <see cref="IStationProfileService"/>, and there is no text box on this page.</description></item>
    /// <item><description>"Change site, line, station and machine number will require
    /// admin access", and separately "Change product type will need Level 3 access" -
    /// so those five are Level 3.</description></item>
    /// <item><description>"Change language will not require admin access" - so language
    /// is the one field editable below Level 3.</description></item>
    /// </list>
    /// </remarks>
    public partial class AudioSettingsViewModel : AudioPageViewModel
    {
        public override AppPageNames PageName => AppPageNames.Settings;

        private readonly IStationProfileService _profile;
        private readonly IMachineIdentityService _identity;
        private readonly IDialogService _dialog;
        private readonly IMachineOrchestrator _orchestrator;

        // True while a refused tab is being put back, so the change hooks do not run twice.
        private bool _revertingTab;

        // True while the profile is being copied into the bound properties, so the
        // per-property change hooks do not treat that as an operator edit.
        private bool _loading;


        // ==================================================================
        // Option lists (slide 62: chosen from a menu, never typed)
        // ==================================================================

        public ObservableCollection<string> Sites { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Lines { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Stations { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Languages { get; } = new ObservableCollection<string>();

        /// <summary>Machine numbers. Slide 62 shows a small stepper; a closed list keeps the no-typing rule.</summary>
        public ObservableCollection<int> MachineNumbers { get; } = new ObservableCollection<int>();

        /// <summary>The four options slide 62's Prod Type menu lists, in its order.</summary>
        public ObservableCollection<ProductSku> ProductTypes { get; } =
            new ObservableCollection<ProductSku>
            {
                ProductSku.M,
                ProductSku.E,
                ProductSku.Normal,
                ProductSku.Common
            };


        // ==================================================================
        // General block
        // ==================================================================

        [ObservableProperty]
        private string _site;

        [ObservableProperty]
        private string _line;

        [ObservableProperty]
        private string _station;

        [ObservableProperty]
        private int _machineNumber;

        [ObservableProperty]
        private string _language;

        [ObservableProperty]
        private ProductSku _productType;

        partial void OnSiteChanged(string value) => MarkDirty();
        partial void OnLineChanged(string value) => MarkDirty();
        partial void OnStationChanged(string value) => MarkDirty();
        partial void OnMachineNumberChanged(int value) => MarkDirty();
        partial void OnLanguageChanged(string value) => MarkDirty();
        partial void OnProductTypeChanged(ProductSku value) => MarkDirty();

        [ObservableProperty]
        private bool _isDirty;

        private void MarkDirty()
        {
            if (_loading)
                return;

            IsDirty = true;
            SaveCommand.NotifyCanExecuteChanged();
            RevertCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsDirtyChanged(bool value)
        {
            SaveCommand.NotifyCanExecuteChanged();
            RevertCommand.NotifyCanExecuteChanged();
        }


        // ==================================================================
        // Access gates
        // ==================================================================

        /// <summary>
        /// Site, line, station, machine number and product type. Slide 62: admin access
        /// for the first four, Level 3 for product type - the same gate either way.
        /// </summary>
        public bool CanEditIdentity => CanEditCritical;

        /// <summary>
        /// Language. Slide 62: "Change language will not require admin access". Allowed
        /// from Level 1 up; still not before login, because an unidentified operator
        /// changing the machine's language is a change with no name against it.
        /// </summary>
        public bool CanEditLanguage => IsLoggedIn;

        protected override void OnAccessLevelChanged(AccessLevel level)
        {
            OnPropertyChanged(nameof(CanEditIdentity));
            OnPropertyChanged(nameof(CanEditLanguage));
            OnPropertyChanged(nameof(AccessHint));

            if (AccessControl != null)
            {
                ApplyLevelToTabs();
                BuildTabs();
            }

            // Dropping below Level 3 with the hardware dialog open closes it unsaved.
            if (!CanEditCritical && IsHardwareDialogOpen)
                IsHardwareDialogOpen = false;

            ChangeConfigCommand.NotifyCanExecuteChanged();
            ApplyHardwareConfigCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
        }

        /// <summary>One line telling the operator what their level lets them change here.</summary>
        public string AccessHint
        {
            get
            {
                if (BypassesAccessLevels)
                    return "Access levels are bypassed (BypassAccessLevels in App.config): every "
                           + "setting on this page can be changed.";

                if (!IsLoggedIn)
                    return "Sign in to change any of these settings.";

                return CanEditCritical
                    ? "Level 3: every setting on this page can be changed."
                    : "Level " + (int)AccessLevel + ": language only. Site, line, station, machine "
                      + "number and product type need Level 3.";
            }
        }


        // ==================================================================
        // Resolved hardware configuration (right pane, slides 62 and 64)
        // ==================================================================

        /// <summary>
        /// "Once saved, the actual config of this machine will show here" (slide 64).
        /// </summary>
        [ObservableProperty]
        private string _resolvedConfig = string.Empty;


        // ==================================================================
        // Hardware Setting dialog (slide 63, and S2.4 for Remote Qualification)
        // ==================================================================

        /// <summary>Whether the Hardware Setting pop-up is showing.</summary>
        [ObservableProperty]
        private bool _isHardwareDialogOpen;

        /// <summary>
        /// The dialog's Remote Qualification choice. A draft until OK: Cancel leaves the
        /// saved value alone. OFF is the default (S2.4: "Remote Qualification mode is
        /// 'OFF' by default").
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RemoteQualificationOff))]
        private bool _remoteQualificationDraft;

        /// <summary>
        /// The OFF radio button's side of <see cref="RemoteQualificationDraft"/>. Only a
        /// check is acted on; the uncheck the radio group sends when ON is picked is
        /// ignored, so the two buttons cannot fight.
        /// </summary>
        public bool RemoteQualificationOff
        {
            get { return !RemoteQualificationDraft; }
            set
            {
                if (value)
                    RemoteQualificationDraft = false;
            }
        }

        /// <summary>One row per subsystem, each with its type chosen from a closed list.</summary>
        public ObservableCollection<HardwareSettingRow> HardwareRows { get; } =
            new ObservableCollection<HardwareSettingRow>();

        public bool HasHardwareRows => HardwareRows.Count > 0;


        // ==================================================================
        // Commands
        // ==================================================================

        public IRelayCommand SaveCommand { get; }

        public IRelayCommand RevertCommand { get; }

        public IRelayCommand ChangeConfigCommand { get; }

        /// <summary>The dialog's OK (slide 63: "Click Load Config to save").</summary>
        public IRelayCommand ApplyHardwareConfigCommand { get; }

        public IRelayCommand CancelHardwareConfigCommand { get; }

        private bool CanSave()
        {
            return IsDirty && IsLoggedIn;
        }

        private void Save()
        {
            if (_profile == null)
            {
                if (_dialog != null)
                    _dialog.ShowWarning("No station profile is configured on this machine.", "Setting");
                return;
            }

            StationProfile draft = _profile.Current.Clone();

            // Only the fields this operator's level permits are copied into the draft.
            // Gating the controls is the affordance; gating the write is the rule - a
            // level change between opening the page and pressing Save must not let an
            // edit through.
            if (CanEditIdentity)
            {
                draft.Site = Site;
                draft.Line = Line;
                draft.Station = Station;
                draft.MachineNumber = MachineNumber;
                draft.ProductType = ProductType;
            }

            if (CanEditLanguage)
                draft.Language = Language;

            string error;
            if (!_profile.Save(draft, out error))
            {
                if (_dialog != null)
                    _dialog.ShowError("The settings could not be saved: " + error, "Setting");
                return;
            }

            IsDirty = false;
            Load();
        }

        private void Revert()
        {
            Load();
            IsDirty = false;
        }

        /// <summary>
        /// Slide 63's hardware dialog - remote qualification on/off and a hardware type
        /// per subsystem, behind "Click to popup window for hardware configuration
        /// (Admin access required)".
        /// </summary>
        /// <remarks>
        /// Opens on a draft of the saved profile. The subsystems and their types come
        /// from the options file; a saved type that is no longer offered falls back to
        /// the subsystem's first option, which is what OK would then save.
        /// <para>
        /// Nothing in this application switches code blocks on the chosen types yet
        /// ("Software backend will auto switch code block according", slide 64): the
        /// choice is recorded, shown in the Config pane and available to that backend.
        /// </para>
        /// </remarks>
        private void ChangeConfig()
        {
            if (_profile == null)
            {
                if (_dialog != null)
                    _dialog.ShowWarning("No station profile is configured on this machine.", "Change Config");
                return;
            }

            // The dialog saves the whole profile, and saving reloads the General block,
            // so unsaved General edits would be lost without a word.
            if (IsDirty)
            {
                if (_dialog != null)
                    _dialog.ShowInfo("Save or revert the General changes first, then open Change Config.", "Change Config");
                return;
            }

            StationProfile current = _profile.Current;
            RemoteQualificationDraft = current.RemoteQualification;

            HardwareRows.Clear();
            foreach (HardwareSubsystem subsystem in _profile.HardwareSubsystems)
            {
                string saved = current.HardwareTypeOf(subsystem.Name);
                string selected = subsystem.Types.FirstOrDefault(t => string.Equals(t, saved, StringComparison.OrdinalIgnoreCase))
                                  ?? subsystem.Types.FirstOrDefault();
                HardwareRows.Add(new HardwareSettingRow(subsystem.Name, subsystem.Types, selected));
            }
            OnPropertyChanged(nameof(HasHardwareRows));

            IsHardwareDialogOpen = true;
        }

        private void ApplyHardwareConfig()
        {
            // Gate the write as well as the button, as Save does.
            if (_profile == null || !CanEditCritical)
            {
                IsHardwareDialogOpen = false;
                return;
            }

            StationProfile draft = _profile.Current.Clone();
            draft.RemoteQualification = RemoteQualificationDraft;
            draft.Hardware = HardwareRows
                .Where(r => !string.IsNullOrWhiteSpace(r.SelectedType))
                .Select(r => new HardwareSelection { Subsystem = r.Subsystem, Type = r.SelectedType })
                .ToList();

            string error;
            if (!_profile.Save(draft, out error))
            {
                // Stays open so the choices are not lost.
                if (_dialog != null)
                    _dialog.ShowError("The hardware configuration could not be saved: " + error, "Change Config");
                return;
            }

            IsHardwareDialogOpen = false;
        }

        private void CancelHardwareConfig()
        {
            IsHardwareDialogOpen = false;
        }

        /// <summary>
        /// Coming back to the page on the Manual tab puts the machine back in manual mode.
        /// The other tabs re-read what they show when they are selected, as before.
        /// </summary>
        public override void OnNavigatedTo()
        {
            base.OnNavigatedTo();

            // The machine started while the operator was elsewhere: do not go back into Manual.
            if (IsManualTab && ManualBlocked)
            {
                SelectedTab = GeneralTab;
                if (_dialog != null)
                    _dialog.ShowWarning(ManualBlockedMessage, "Manual");
            }

            if (IsManualTab)
                Manual.OnShown();
        }

        /// <summary>Leaving the page abandons an open Hardware Setting dialog.</summary>
        public override void OnNavigatedFrom()
        {
            IsHardwareDialogOpen = false;
            SettingsTabBase tab = TabFor(SelectedTab);
            if (tab != null)
                tab.OnHidden();
            base.OnNavigatedFrom();
        }


        // ==================================================================
        // Tabs: General, Manual, Access Control, Valve Model, HIVE Control, Picture Qty.
        // Check, PDATA Mapping (Level 3) and Hash Key. Which are offered depends on the level (and, for Hash
        // Key, on the HIVE state), so the list is rebuilt when either changes.
        // ==================================================================

        public const string GeneralTab = "General";

        private readonly IHiveControl _hive;

        public ObservableCollection<string> Tabs { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsGeneralTab), nameof(IsManualTab), nameof(IsAccessControlTab), nameof(IsValveModelTab),
            nameof(IsHiveControlTab), nameof(IsPictureQtyTab), nameof(IsHashKeyTab), nameof(IsPdataMappingTab), nameof(IsLocalizationTab), nameof(IsConfigDescriptionTab),
            nameof(IsEarlyWarningTab), nameof(IsMaintenanceTab))]
        private string _selectedTab = GeneralTab;

        public bool IsGeneralTab => SelectedTab == GeneralTab;
        public bool IsManualTab => Manual != null && SelectedTab == Manual.Title;
        public bool IsAccessControlTab => AccessControl != null && SelectedTab == AccessControl.Title;
        public bool IsValveModelTab => ValveModel != null && SelectedTab == ValveModel.Title;
        public bool IsHiveControlTab => HiveControl != null && SelectedTab == HiveControl.Title;
        public bool IsPictureQtyTab => PictureQty != null && SelectedTab == PictureQty.Title;
        public bool IsHashKeyTab => HashKey != null && SelectedTab == HashKey.Title;
        public bool IsPdataMappingTab => PdataMapping != null && SelectedTab == PdataMapping.Title;
        public bool IsLocalizationTab => Localization != null && SelectedTab == Localization.Title;
        public bool IsConfigDescriptionTab => ConfigDescription != null && SelectedTab == ConfigDescription.Title;
        public bool IsEarlyWarningTab => EarlyWarning != null && SelectedTab == EarlyWarning.Title;
        public bool IsMaintenanceTab => Maintenance != null && SelectedTab == Maintenance.Title;

        /// <summary>The manual-operation GUI, with the Homing button and the running log.</summary>
        public ManualTabViewModel Manual { get; }

        public AccessControlTabViewModel AccessControl { get; }
        public ValveModelTabViewModel ValveModel { get; }
        public HiveControlTabViewModel HiveControl { get; }
        public PictureQtyTabViewModel PictureQty { get; }
        public HashKeyTabViewModel HashKey { get; }
        public PdataMappingTabViewModel PdataMapping { get; }

        /// <summary>S17: the early warning's test box and its enable / disable switch.</summary>
        public EarlyWarningTabViewModel EarlyWarning { get; }

        /// <summary>S11.3: the maintenance items of each fixture, with their countdown and health.</summary>
        public MaintenanceTabViewModel Maintenance { get; }

        /// <summary>The English, Chinese and Vietnamese wording of every string on screen.</summary>
        public LocalizationEditorTabViewModel Localization { get; }

        /// <summary>The I/O point and spec limit names per language (as Odyssey.Master_V3).</summary>
        public ConfigDescriptionTabViewModel ConfigDescription { get; }

        /// <summary>
        /// True while the auto sequence is running (or paused part-way): the Manual screen
        /// cannot be opened then, because manual mode would take the PLC out of the sequence.
        /// </summary>
        private bool ManualBlocked => _orchestrator != null && _orchestrator.IsAutoRunning;

        /// <summary>The PLC is homing, or the Homing button was pressed and the PLC has not finished.</summary>
        private bool IsHomingInProgress =>
            (Manual != null && Manual.IsHoming)
            || (_orchestrator != null && _orchestrator.CurrentStatus == Nova.Abstractions.Machine.Enums.MachineStatus.Homing);

        private const string ManualBlockedMessage =
            "The Manual screen cannot be opened while the auto sequence is running. Stop the machine first.";

        /// <summary>The auto sequence started while the Manual tab was open: leave it, which releases manual mode.</summary>
        private void OnMachineStatusChanged(Nova.Abstractions.Machine.Enums.MachineStatus status)
        {
            if (IsManualTab && ManualBlocked)
                SelectedTab = GeneralTab;
        }

        partial void OnSelectedTabChanged(string oldValue, string newValue)
        {
            // A refused tab was put back: nothing changed, so there is nothing to show or hide.
            if (_revertingTab)
                return;

            // FAM 2 Bug_Id_004: leaving the Manual tab releases manual mode, which stops a
            // homing in progress. Stay on it until the homing ends.
            if (Manual != null && oldValue == Manual.Title && newValue != Manual.Title && IsHomingInProgress)
            {
                _revertingTab = true;
                try
                {
                    SelectedTab = oldValue;
                }
                finally
                {
                    _revertingTab = false;
                }

                if (_dialog != null)
                    _dialog.ShowWarning("Homing is in progress. Wait for it to finish before leaving this screen.", "Homing");
                return;
            }

            if (Manual != null && newValue == Manual.Title && ManualBlocked)
            {
                _revertingTab = true;
                try
                {
                    SelectedTab = string.IsNullOrEmpty(oldValue) ? GeneralTab : oldValue;
                }
                finally
                {
                    _revertingTab = false;
                }

                if (_dialog != null)
                    _dialog.ShowWarning(ManualBlockedMessage, "Manual");
                return;
            }

            // The Hardware Setting pop-up belongs to General.
            if (newValue != GeneralTab)
                IsHardwareDialogOpen = false;

            // The new tab first, so a tab that holds manual mode takes it before the old one lets go.
            SettingsTabBase after = TabFor(newValue);
            if (after != null)
                after.OnShown();

            SettingsTabBase before = TabFor(oldValue);
            if (before != null)
                before.OnHidden();
        }

        private SettingsTabBase TabFor(string title)
        {
            if (title == null || AccessControl == null)
                return null;
            if (title == Manual.Title) return Manual;
            if (title == AccessControl.Title) return AccessControl;
            if (title == ValveModel.Title) return ValveModel;
            if (title == HiveControl.Title) return HiveControl;
            if (title == PictureQty.Title) return PictureQty;
            if (title == HashKey.Title) return HashKey;
            if (title == PdataMapping.Title) return PdataMapping;
            if (title == Localization.Title) return Localization;
            if (title == ConfigDescription.Title) return ConfigDescription;
            if (title == EarlyWarning.Title) return EarlyWarning;
            if (title == Maintenance.Title) return Maintenance;
            return null;
        }

        private void ApplyLevelToTabs()
        {
            Manual.Level = AccessLevel;
            AccessControl.Level = AccessLevel;
            ValveModel.Level = AccessLevel;
            HiveControl.Level = AccessLevel;
            PictureQty.Level = AccessLevel;
            HashKey.Level = AccessLevel;
            PdataMapping.Level = AccessLevel;
            Localization.Level = AccessLevel;
            ConfigDescription.Level = AccessLevel;
            EarlyWarning.Level = AccessLevel;
            Maintenance.Level = AccessLevel;
        }

        /// <summary>
        /// Picture Qty. Check: "only displayed on the GUI when the Level 2 and Level 3
        /// user logs in" (T5). Hash Key: "only displayed on the GUI when the Level 3 user
        /// logs in and changes the HIVE state to the planned DT (PD-15)" (slide 80). On a
        /// machine without HIVE there is no PD-15 to enter, so Level 3 is enough there.
        /// </summary>
        private void BuildTabs()
        {
            var wanted = new List<string> { GeneralTab, Manual.Title };
            if (IsLoggedIn)
                wanted.Add(AccessControl.Title);
            wanted.Add(ValveModel.Title);
            if (IsLoggedIn)
                wanted.Add(HiveControl.Title);
            if (CanEdit)
                wanted.Add(PictureQty.Title);
            // S11.3: "Level 2 / Level 3 user logs in, click to activate this page".
            if (CanEdit && Maintenance.IsAvailable)
                wanted.Add(Maintenance.Title);
            // S17: the test box and the switch; the switch itself asks for the CM's card.
            if (CanEdit && EarlyWarning.IsPresent)
                wanted.Add(EarlyWarning.Title);
            if (CanEditCritical)
                wanted.Add(PdataMapping.Title);
            if (CanEditCritical && (_hive == null || !_hive.IsAvailable
                                    || string.Equals(_hive.CurrentDowntimeCode, "PD-15", StringComparison.OrdinalIgnoreCase)))
                wanted.Add(HashKey.Title);
            // Anyone signed in can see the wording; only Level 3 can change it.
            if (IsLoggedIn && ConfigDescription.IsAvailable)
                wanted.Add(ConfigDescription.Title);
            if (IsLoggedIn && Localization.IsAvailable)
                wanted.Add(Localization.Title);

            if (Tabs.SequenceEqual(wanted))
                return;

            string selected = SelectedTab;
            Tabs.Clear();
            foreach (string tab in wanted)
                Tabs.Add(tab);

            SelectedTab = wanted.Contains(selected) ? selected : GeneralTab;
        }

        private void OnHiveChanged(object sender, EventArgs e)
        {
            RunOnUi(BuildTabs);
        }

        // ==================================================================

        public AudioSettingsViewModel(
            IAppSession session,
            IStationProfileService profile = null,
            IMachineIdentityService identity = null,
            IDialogService dialog = null,
            IAccessControlAdmin accessAdmin = null,
            IAccessControlService access = null,
            IFileDialogService files = null,
            IAccessLog accessLog = null,
            IConsumableStore consumables = null,
            IHiveControl hive = null,
            IBadgePrompt badges = null,
            IPictureQuantityCheck pictures = null,
            IHashKeyService hashes = null,
            IMesClient mes = null,
            IPdataMappingService pdata = null,
            // The Manual tab: the manual-operation GUI (its Log tab has the running log),
            // the Homing button, and the machine's manual mode while the tab is on screen.
            ManualViewModel manual = null,
            IHomingService homing = null,
            IMachineOrchestrator orchestrator = null,
            // The Localization Editor tab: the language files behind the dual language.
            ILocalizationEditorService localizationEditor = null,
            ILocalizationService localization = null,
            // The Config Description tab: I/O point and spec limit names per language.
            IDescriptionService descriptions = null,
            // The Early Warning tab (S17) and the Maintenance tab (S11.3).
            IEarlyWarning earlyWarning = null,
            IMaintenanceStore maintenance = null,
            ICsvExportService csvExport = null)
            : base(session, AccessBypass.ConfigAndSettings)
        {
            _profile = profile;
            _identity = identity;
            _dialog = dialog;
            _hive = hive;
            _orchestrator = orchestrator;

            Manual = new ManualTabViewModel(manual, homing, orchestrator, dialog, session, accessLog);
            AccessControl = new AccessControlTabViewModel(accessAdmin, access, files, dialog, session, accessLog);
            ValveModel = new ValveModelTabViewModel(consumables, dialog, session, accessLog);
            HiveControl = new HiveControlTabViewModel(hive, badges, dialog, session, accessLog);
            PictureQty = new PictureQtyTabViewModel(pictures, badges, dialog, session, accessLog);
            HashKey = new HashKeyTabViewModel(hashes, mes, badges, files, dialog, session, accessLog);
            PdataMapping = new PdataMappingTabViewModel(pdata, dialog, session, accessLog);
            Localization = new LocalizationEditorTabViewModel(localizationEditor, localization, dialog);
            ConfigDescription = new ConfigDescriptionTabViewModel(descriptions, localization, dialog);
            EarlyWarning = new EarlyWarningTabViewModel(earlyWarning, dialog, session, accessLog);
            Maintenance = new MaintenanceTabViewModel(maintenance, mes, hive, csvExport, dialog, session, accessLog, profile,
                () => accessAdmin != null && accessAdmin.Settings != null ? accessAdmin.Settings.ProjectCode : string.Empty);

            if (_hive != null)
                _hive.Changed += OnHiveChanged;

            if (_orchestrator != null)
                _orchestrator.OnMachineStatusChanged += OnMachineStatusChanged;

            ApplyLevelToTabs();
            BuildTabs();

            SaveCommand = new RelayCommand(Save, CanSave);
            RevertCommand = new RelayCommand(Revert, () => IsDirty);
            ChangeConfigCommand = new RelayCommand(ChangeConfig, () => CanEditCritical);
            ApplyHardwareConfigCommand = new RelayCommand(ApplyHardwareConfig, () => CanEditCritical);
            CancelHardwareConfigCommand = new RelayCommand(CancelHardwareConfig);

            if (_profile != null)
            {
                Fill(Sites, _profile.Sites);
                Fill(Lines, _profile.Lines);
                Fill(Stations, _profile.Stations);
                Fill(Languages, _profile.Languages);
                foreach (int number in _profile.MachineNumbers ?? (IReadOnlyList<int>)Array.Empty<int>())
                    MachineNumbers.Add(number);
                _profile.ProfileChanged += OnProfileChanged;
            }

            Load();
            BuildResolvedConfig();
        }

        private static void Fill(ObservableCollection<string> target, IReadOnlyList<string> source)
        {
            target.Clear();
            if (source == null)
                return;

            foreach (string item in source)
                target.Add(item);
        }

        private void OnProfileChanged(object sender, EventArgs e)
        {
            Load();
            BuildResolvedConfig();
        }

        /// <summary>
        /// Copies the stored profile into the bound fields.
        /// </summary>
        /// <remarks>
        /// A stored value that is not in its option list is appended to that list before
        /// being selected. Otherwise the combo would silently bind to null and the page
        /// would misreport the machine's own configuration as blank - and a Save would
        /// then write that blank back.
        /// </remarks>
        private void Load()
        {
            if (_profile == null)
                return;

            StationProfile p = _profile.Current;
            if (p == null)
                return;

            _loading = true;
            try
            {
                Site = Select(Sites, p.Site);
                Line = Select(Lines, p.Line);
                Station = Select(Stations, p.Station);
                Language = Select(Languages, p.Language);

                if (!MachineNumbers.Contains(p.MachineNumber) && p.MachineNumber > 0)
                    MachineNumbers.Add(p.MachineNumber);
                MachineNumber = p.MachineNumber;

                ProductType = p.ProductType;
            }
            finally
            {
                _loading = false;
            }
        }

        private static string Select(ObservableCollection<string> options, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (!options.Contains(value))
                options.Add(value);

            return value;
        }

        /// <summary>
        /// The right pane's text block (slides 62 and 64): "Once saved, the actual config
        /// of this machine will show here".
        /// </summary>
        /// <remarks>
        /// IPC is read from the PC it is running on. Each hardware subsystem is shown
        /// with the type saved from the Hardware Setting dialog - what this machine has
        /// been configured as, not a probe of the hardware.
        /// </remarks>
        private void BuildResolvedConfig()
        {
            var text = new StringBuilder();
            StationProfile profile = _profile != null ? _profile.Current : null;

            text.AppendLine("Config");
            text.AppendLine();

            text.AppendLine("IPC");
            text.AppendLine("-----");
            text.AppendLine("Host: " + SafeMachineName());
            text.AppendLine("Processors: " + Environment.ProcessorCount);
            foreach (string disk in FixedDisks())
                text.AppendLine(disk);
            text.AppendLine("OS: " + Environment.OSVersion.VersionString);
            text.AppendLine("Runtime: " + Environment.Version);
            text.AppendLine();

            text.AppendLine("Machine");
            text.AppendLine("-----");
            if (profile != null)
                text.AppendLine("SW version: " + profile.SoftwareVersion);
            if (_identity != null)
            {
                text.AppendLine("Name: " + _identity.MachineName);
                text.AppendLine("MC version: " + _identity.MachineVersion);
            }
            if (profile != null)
                text.AppendLine("Remote Qualification: " + (profile.RemoteQualification ? "ON" : "OFF"));
            text.AppendLine();

            IReadOnlyList<HardwareSubsystem> subsystems = _profile != null
                ? _profile.HardwareSubsystems
                : (IReadOnlyList<HardwareSubsystem>)Array.Empty<HardwareSubsystem>();

            if (subsystems.Count == 0)
            {
                text.AppendLine("Hardware");
                text.AppendLine("-----");
                text.AppendLine("No hardware subsystems are listed in Data\\Settings\\StationOptions.json.");
            }

            foreach (HardwareSubsystem subsystem in subsystems)
            {
                string type = profile != null ? profile.HardwareTypeOf(subsystem.Name) : null;

                text.AppendLine(subsystem.Name);
                text.AppendLine("-----");
                text.AppendLine("Type: " + (string.IsNullOrWhiteSpace(type) ? "not set - use Change Config" : type));
                text.AppendLine();
            }

            ResolvedConfig = text.ToString().TrimEnd();
        }

        /// <summary>"Disk C: 256 GB", one line per fixed drive; the slide's Hard Disk lines.</summary>
        private static IEnumerable<string> FixedDisks()
        {
            var lines = new List<string>();
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                        continue;

                    long gigabytes = (long)Math.Round(drive.TotalSize / 1e9);
                    lines.Add("Disk " + drive.Name.TrimEnd('\\') + " " + gigabytes + " GB");
                }
            }
            catch (Exception)
            {
                // A drive that cannot be read must not stop the Setting page rendering.
            }

            return lines;
        }

        private static string SafeMachineName()
        {
            try
            {
                return Environment.MachineName;
            }
            catch (Exception)
            {
                // Environment.MachineName throws if the computer name is unset; the
                // Setting page must still render.
                return "unknown";
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _profile != null)
                _profile.ProfileChanged -= OnProfileChanged;

            if (disposing && _hive != null)
                _hive.Changed -= OnHiveChanged;

            if (disposing)
            {
                Manual.Dispose();
                Maintenance.Dispose();
            }

            base.Dispose(disposing);
        }

        private readonly System.Threading.SynchronizationContext _ui = System.Threading.SynchronizationContext.Current;

        /// <summary>HIVE changes arrive on the thread that caused them; the tab list is bound.</summary>
        private void RunOnUi(Action action)
        {
            // Always posted: WPF may give the UI thread a different context instance.
            if (_ui == null)
                action();
            else
                _ui.Post(_ => action(), null);
        }
    }
}
