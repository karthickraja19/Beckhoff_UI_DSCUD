using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

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
        public ObservableCollection<int> MachineNumbers { get; } =
            new ObservableCollection<int> { 1, 2, 3, 4, 5, 6, 7, 8 };

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

            ChangeConfigCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
        }

        /// <summary>One line telling the operator what their level lets them change here.</summary>
        public string AccessHint
        {
            get
            {
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
        // Commands
        // ==================================================================

        public IRelayCommand SaveCommand { get; }

        public IRelayCommand RevertCommand { get; }

        public IRelayCommand ChangeConfigCommand { get; }

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
        /// The dialog itself is not built. Nothing in this application resolves a
        /// hardware type to a code block yet ("Software backend will auto switch code
        /// block according", slide 64), so a dialog that let an engineer pick Type B
        /// would record a choice the machine then ignored - worse than not offering it.
        /// </remarks>
        private void ChangeConfig()
        {
            if (_dialog == null)
                return;

            _dialog.ShowInfo(
                "The hardware configuration dialog is not built yet. It will hold the remote "
                + "qualification switch and the per-subsystem hardware type selection from "
                + "DFM v5.4 slide 63, once the software can switch code blocks to match.",
                "Change Config");
        }

        // ==================================================================

        public AudioSettingsViewModel(
            IAppSession session,
            IStationProfileService profile = null,
            IMachineIdentityService identity = null,
            IDialogService dialog = null)
            : base(session)
        {
            _profile = profile;
            _identity = identity;
            _dialog = dialog;

            SaveCommand = new RelayCommand(Save, CanSave);
            RevertCommand = new RelayCommand(Revert, () => IsDirty);
            ChangeConfigCommand = new RelayCommand(ChangeConfig, () => CanEditCritical);

            if (_profile != null)
            {
                Fill(Sites, _profile.Sites);
                Fill(Lines, _profile.Lines);
                Fill(Stations, _profile.Stations);
                Fill(Languages, _profile.Languages);
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
        /// The right pane's text block (slides 62 and 64): IPC, CCD, PLC and Driver.
        /// </summary>
        /// <remarks>
        /// Reports what this install actually knows - the machine identity from the
        /// profile and the host it is running on. Subsystem detail (CCD resolutions,
        /// conveyor and robot-arm types) comes from the hardware configuration that
        /// slide 63's dialog would set, which does not exist yet, so those lines say so
        /// rather than printing the slide's example values as if they were read from
        /// this machine.
        /// </remarks>
        private void BuildResolvedConfig()
        {
            var text = new StringBuilder();

            text.AppendLine("Config");
            text.AppendLine();

            text.AppendLine("IPC");
            text.AppendLine("-----");
            text.AppendLine("Host: " + SafeMachineName());
            text.AppendLine("Processors: " + Environment.ProcessorCount);
            text.AppendLine("OS: " + Environment.OSVersion.VersionString);
            text.AppendLine("Runtime: " + Environment.Version);
            text.AppendLine();

            text.AppendLine("Machine");
            text.AppendLine("-----");
            if (_identity != null)
            {
                text.AppendLine("Name: " + _identity.MachineName);
                text.AppendLine("SW version: " + _identity.SoftwareVersion);
                text.AppendLine("MC version: " + _identity.MachineVersion);
            }
            else
            {
                text.AppendLine("No machine profile was read.");
            }
            text.AppendLine();

            text.AppendLine("CCD");
            text.AppendLine("-----");
            text.AppendLine("Not configured - set from Change Config.");
            text.AppendLine();

            text.AppendLine("PLC");
            text.AppendLine("-----");
            text.AppendLine("Not configured - set from Change Config.");
            text.AppendLine();

            text.AppendLine("Driver");
            text.AppendLine("-----");
            text.AppendLine("Not configured - set from Change Config.");

            ResolvedConfig = text.ToString();
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
