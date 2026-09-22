using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

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

        /// <summary>Leaving the page abandons an open Hardware Setting dialog.</summary>
        public override void OnNavigatedFrom()
        {
            IsHardwareDialogOpen = false;
            base.OnNavigatedFrom();
        }

        // ==================================================================

        public AudioSettingsViewModel(
            IAppSession session,
            IStationProfileService profile = null,
            IMachineIdentityService identity = null,
            IDialogService dialog = null)
            : base(session, AccessBypass.ConfigAndSettings)
        {
            _profile = profile;
            _identity = identity;
            _dialog = dialog;

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

            base.Dispose(disposing);
        }
    }
}
