using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>One editable row of the local access list.</summary>
    public partial class AccessRecordRow : ObservableObject
    {
        [ObservableProperty]
        private string _site = string.Empty;

        [ObservableProperty]
        private string _project = string.Empty;

        [ObservableProperty]
        private string _stationId = string.Empty;

        [ObservableProperty]
        private string _terminalName = string.Empty;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private int _accessLevel = 1;

        [ObservableProperty]
        private string _softwareType = "Main";

        [ObservableProperty]
        private string _cardUid = string.Empty;

        [ObservableProperty]
        private string _function = string.Empty;

        [ObservableProperty]
        private AccessOrganization _organization;

        public static AccessRecordRow From(AccessRecord record)
        {
            return new AccessRecordRow
            {
                Site = record.Site ?? string.Empty,
                Project = record.Project ?? string.Empty,
                StationId = record.StationId ?? string.Empty,
                TerminalName = record.TerminalName ?? string.Empty,
                Name = record.Name ?? string.Empty,
                AccessLevel = record.AccessLevel,
                SoftwareType = record.SoftwareType ?? string.Empty,
                CardUid = record.CardUid ?? string.Empty,
                Function = record.Function ?? string.Empty,
                Organization = record.Organization,
            };
        }

        public AccessRecord ToRecord()
        {
            return new AccessRecord
            {
                Site = (Site ?? string.Empty).Trim(),
                Project = (Project ?? string.Empty).Trim(),
                StationId = (StationId ?? string.Empty).Trim(),
                TerminalName = (TerminalName ?? string.Empty).Trim().ToUpperInvariant(),
                Name = (Name ?? string.Empty).Trim(),
                AccessLevel = AccessLevel,
                SoftwareType = (SoftwareType ?? string.Empty).Trim(),
                CardUid = (CardUid ?? string.Empty).Trim(),
                Function = (Function ?? string.Empty).Trim(),
                Organization = Organization,
            };
        }
    }

    /// <summary>
    /// The Access Control tab of the Setting page - Access Control V5.1, S1.2.2 (the
    /// "Access Control with MES" switch) and S1.2.3 (the local access list: Import CSV,
    /// Edit, Save at Level 3).
    /// </summary>
    public partial class AccessControlTabViewModel : SettingsTabBase
    {
        private readonly IAccessControlAdmin _admin;
        private readonly IAccessControlService _access;
        private readonly IFileDialogService _files;
        private readonly IDialogService _dialog;
        private readonly IAppSession _session;
        private readonly IAccessLog _log;

        public AccessControlTabViewModel(
            IAccessControlAdmin admin,
            IAccessControlService access,
            IFileDialogService files,
            IDialogService dialog,
            IAppSession session,
            IAccessLog log)
        {
            _admin = admin;
            _access = access;
            _files = files;
            _dialog = dialog;
            _session = session;
            _log = log;

            EditCommand = new RelayCommand(Edit, () => CanEditCritical && !IsEditing && IsAvailable);
            SaveCommand = new RelayCommand(Save, () => CanEditCritical && IsEditing);
            CancelCommand = new RelayCommand(Cancel, () => IsEditing);
            AddRowCommand = new RelayCommand(AddRow, () => IsEditing);
            DeleteRowCommand = new RelayCommand(DeleteRow, () => IsEditing && SelectedRow != null);
            ImportCommand = new RelayCommand(Import, () => CanEditCritical && IsAvailable);
            ExportCommand = new RelayCommand(Export, () => IsLoggedIn && Rows.Count > 0);
            SaveSettingsCommand = new RelayCommand(SaveSettings, () => CanEditCritical && IsSettingsDirty);
            RevertSettingsCommand = new RelayCommand(LoadSettings, () => IsSettingsDirty);

            if (_admin != null)
                _admin.SettingsChanged += (sender, e) => OnUi(LoadSettings);

            Load();
            LoadSettings();
        }

        public override string Title => "Access Control";

        public bool IsAvailable => _admin != null;

        public string TerminalName => _access != null ? _access.TerminalName : string.Empty;

        public string SoftwareType => _access != null ? _access.SoftwareType : string.Empty;

        // ------------------------------------------------------------------
        // Local access list
        // ------------------------------------------------------------------

        public ObservableCollection<AccessRecordRow> Rows { get; } = new ObservableCollection<AccessRecordRow>();

        public IReadOnlyList<int> Levels { get; } = new[] { 1, 2, 3 };

        /// <summary>V5.1: Main and Vision sign in; Authorize approves a two-step verification.</summary>
        public IReadOnlyList<string> SoftwareTypes { get; } = new[] { "Main", "Vision", "Authorize" };

        public IReadOnlyList<AccessOrganization> Organizations { get; } =
            (AccessOrganization[])Enum.GetValues(typeof(AccessOrganization));

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteRowCommand))]
        private AccessRecordRow _selectedRow;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsReadOnly))]
        [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(SaveCommand), nameof(CancelCommand), nameof(AddRowCommand), nameof(DeleteRowCommand))]
        private bool _isEditing;

        public bool IsReadOnly => !IsEditing;

        [ObservableProperty]
        private string _listStatus = string.Empty;

        public IRelayCommand EditCommand { get; }
        public IRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand AddRowCommand { get; }
        public IRelayCommand DeleteRowCommand { get; }
        public IRelayCommand ImportCommand { get; }
        public IRelayCommand ExportCommand { get; }

        // ------------------------------------------------------------------
        // Settings (V5.1 3.3.1)
        // ------------------------------------------------------------------

        public bool IsMesAvailable => _admin != null && _admin.IsMesAvailable;

        public string MesNote => IsMesAvailable
            ? "ON checks every badge with MES (G_MACHINE_ACCESS_BY_USERNO); OFF checks it against the list below."
            : "MES is not configured on this machine (MesUrl in App.config), so badges are checked against the list below.";

        public ObservableCollection<string> ProjectCodes { get; } = new ObservableCollection<string>();

        public IReadOnlyList<int> SignOutMinutes { get; } = new[] { 1, 2, 3, 4, 5 };

        [ObservableProperty]
        private bool _accessControlWithMes;

        [ObservableProperty]
        private string _projectCode;

        [ObservableProperty]
        private int _automaticSignOutMinutes = 5;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand), nameof(RevertSettingsCommand))]
        private bool _isSettingsDirty;

        private bool _loadingSettings;

        partial void OnAccessControlWithMesChanged(bool value)
        {
            OnPropertyChanged(nameof(AccessControlLocal));
            MarkSettingsDirty();
        }

        /// <summary>
        /// The FALSE radio button's side of <see cref="AccessControlWithMes"/>. Only a check
        /// is acted on, so the two buttons of the group cannot fight.
        /// </summary>
        public bool AccessControlLocal
        {
            get { return !AccessControlWithMes; }
            set
            {
                if (value)
                    AccessControlWithMes = false;
            }
        }
        partial void OnProjectCodeChanged(string value) => MarkSettingsDirty();
        partial void OnAutomaticSignOutMinutesChanged(int value) => MarkSettingsDirty();

        public IRelayCommand SaveSettingsCommand { get; }
        public IRelayCommand RevertSettingsCommand { get; }

        public string AccessHint
        {
            get
            {
                if (!IsLoggedIn)
                    return "Sign in to see the access list.";

                return CanEditCritical
                    ? "Level 3: the list and the settings can be changed. Every change is recorded in the machine log."
                    : "Level " + (int)Level + ": view only. Changing the access list needs Level 3.";
            }
        }

        protected override void OnLevelChanged()
        {
            if (!CanEditCritical && IsEditing)
                Cancel();

            OnPropertyChanged(nameof(AccessHint));
            NotifyCommands();
        }

        public override void OnShown()
        {
            if (!IsEditing)
                Load();
            LoadSettings();
        }

        public override void OnHidden()
        {
            if (IsEditing)
                Cancel();
        }

        private void Load()
        {
            Rows.Clear();
            if (_admin == null)
            {
                ListStatus = "The access list is not available on this machine.";
                return;
            }

            string error;
            IReadOnlyList<AccessRecord> records = _admin.LoadRecords(out error);
            foreach (AccessRecord record in records)
                Rows.Add(AccessRecordRow.From(record));

            ListStatus = error ?? Rows.Count + " record" + (Rows.Count == 1 ? "" : "s") + " - this terminal is " + TerminalName + " (" + SoftwareType + ").";
            NotifyCommands();
        }

        private void Edit()
        {
            IsEditing = true;
        }

        private void Cancel()
        {
            IsEditing = false;
            Load();
        }

        private void AddRow()
        {
            var row = new AccessRecordRow
            {
                TerminalName = TerminalName,
                SoftwareType = SoftwareType,
                Project = ProjectCode ?? string.Empty,
                AccessLevel = 1,
            };
            Rows.Add(row);
            SelectedRow = row;
        }

        private void DeleteRow()
        {
            if (SelectedRow == null)
                return;

            Rows.Remove(SelectedRow);
            SelectedRow = null;
        }

        private void Save()
        {
            if (!CanEditCritical || _admin == null)
                return;

            List<AccessRecord> records = Rows.Select(r => r.ToRecord()).ToList();
            string error;
            if (!_admin.SaveRecords(records, out error))
            {
                _dialog?.ShowError(error, "Access Control");
                return;
            }

            Record("saved the access list (" + records.Count + " records)");
            IsEditing = false;
            Load();
        }

        private void Import()
        {
            if (!CanEditCritical || _admin == null || _files == null)
                return;

            string path = _files.OpenFile("Import access list", "CSV files (*.csv)|*.csv");
            if (path == null)
                return;

            string error, notes;
            IReadOnlyList<AccessRecord> records = _admin.ImportCsv(path, out error, out notes);
            if (records == null)
            {
                _dialog?.ShowError(error, "Import CSV");
                return;
            }

            // The file replaces the list, so the operator sees it before anything is saved.
            if (_dialog != null && !_dialog.Confirm(
                    records.Count + " records were read from " + System.IO.Path.GetFileName(path) + ". They replace the "
                    + Rows.Count + " records on screen; nothing is saved until you press Save. Continue?",
                    "Import CSV"))
            {
                return;
            }

            Rows.Clear();
            foreach (AccessRecord record in records)
                Rows.Add(AccessRecordRow.From(record));

            IsEditing = true;
            ListStatus = records.Count + " records imported from " + System.IO.Path.GetFileName(path) + " - press Save to keep them."
                + (notes != null ? " " + notes : string.Empty);
            Record("imported " + records.Count + " access records from " + path + " (not saved yet)");
        }

        private void Export()
        {
            if (_admin == null || _files == null)
                return;

            string path = _files.SaveFile("Export access list", "CSV files (*.csv)|*.csv",
                "AccessList_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
            if (path == null)
                return;

            string error;
            if (!_admin.ExportCsv(path, Rows.Select(r => r.ToRecord()).ToList(), out error))
            {
                _dialog?.ShowError("The access list could not be exported: " + error, "Export CSV");
                return;
            }

            Record("exported the access list to " + path);
        }

        private void LoadSettings()
        {
            if (_admin == null)
                return;

            AccessControlSettings settings = _admin.Settings;
            _loadingSettings = true;
            try
            {
                ProjectCodes.Clear();
                foreach (string code in settings.ProjectCodes ?? new List<string>())
                {
                    if (!string.IsNullOrWhiteSpace(code) && !ProjectCodes.Contains(code))
                        ProjectCodes.Add(code);
                }

                if (!string.IsNullOrWhiteSpace(settings.ProjectCode) && !ProjectCodes.Contains(settings.ProjectCode))
                    ProjectCodes.Add(settings.ProjectCode);

                AccessControlWithMes = settings.AccessControlWithMes && IsMesAvailable;
                ProjectCode = string.IsNullOrWhiteSpace(settings.ProjectCode) ? null : settings.ProjectCode;
                AutomaticSignOutMinutes = settings.AutomaticSignOutMinutes;
            }
            finally
            {
                _loadingSettings = false;
            }

            IsSettingsDirty = false;
        }

        private void MarkSettingsDirty()
        {
            if (!_loadingSettings)
                IsSettingsDirty = true;
        }

        private void SaveSettings()
        {
            if (!CanEditCritical || _admin == null)
                return;

            AccessControlSettings settings = _admin.Settings;
            settings.AccessControlWithMes = AccessControlWithMes;
            settings.ProjectCode = ProjectCode ?? string.Empty;
            settings.AutomaticSignOutMinutes = AutomaticSignOutMinutes;

            string error;
            if (!_admin.SaveSettings(settings, out error))
            {
                _dialog?.ShowError(error, "Access Control");
                return;
            }

            Record("set Access Control with MES = " + (AccessControlWithMes ? "TRUE" : "FALSE") + ", Project Code = "
                   + settings.ProjectCode + ", Automatically Sign out = " + AutomaticSignOutMinutes + " min");
            LoadSettings();
        }

        private void Record(string action)
        {
            if (_log != null && _session != null)
                _log.Action(_session.UserName, (int)_session.Level, action);
        }

        private void NotifyCommands()
        {
            EditCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            ImportCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
            SaveSettingsCommand.NotifyCanExecuteChanged();
        }
    }
}
