using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>One hash key on the PD-15 page.</summary>
    public partial class HashKeyRow : ObservableObject
    {
        public HashKeyRow(HashKeyDefinition definition, string path)
        {
            Key = definition.Key;
            Controls = definition.Controls;
            _filePath = path ?? string.Empty;
        }

        public string Key { get; }

        public string Controls { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsConfigured))]
        private string _filePath;

        [ObservableProperty]
        private string _value = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        private string _status = string.Empty;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(FilePath);

        public bool HasError { get; private set; }

        public void Apply(HashKeyReading reading)
        {
            if (reading == null)
            {
                Value = string.Empty;
                HasError = false;
                Status = IsConfigured ? "Not read yet" : "Not monitored";
                return;
            }

            Value = reading.Value ?? string.Empty;
            HasError = reading.Value == null;
            Status = reading.Value != null ? "OK" : reading.Error;
        }
    }

    /// <summary>
    /// The hash key page of PD-15 "Uploaded Hash Keys to MES" (Audio SW DFM v5.4 slides
    /// 78-90, S3.6).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>"Only displayed on the GUI when the Level 3 user logs in and changes the HIVE
    /// state to the planned DT (PD-15)" - the Setting page decides that.</item>
    /// <item>"After the 2-Step ID verification, the 'Refresh all hash keys', 'Export all
    /// hash keys', 'Cancel' and 'Upload to MES' buttons will be displayed."</item>
    /// <item>File Path Setting: each key's complete path and file name; no path for a file
    /// that does not exist or is not monitored, and a way to clear a path.</item>
    /// <item>After the upload succeeds and its dialog is confirmed, the buttons are hidden
    /// and the authorized user is logged out.</item>
    /// </list>
    /// </remarks>
    public partial class HashKeyTabViewModel : SettingsTabBase
    {
        private readonly IHashKeyService _hashes;
        private readonly IMesClient _mes;
        private readonly IBadgePrompt _badges;
        private readonly IFileDialogService _files;
        private readonly IDialogService _dialog;
        private readonly IAppSession _session;
        private readonly IAccessLog _log;

        private AccessCheckResult _approver;

        public HashKeyTabViewModel(
            IHashKeyService hashes,
            IMesClient mes,
            IBadgePrompt badges,
            IFileDialogService files,
            IDialogService dialog,
            IAppSession session,
            IAccessLog log)
        {
            _hashes = hashes;
            _mes = mes;
            _badges = badges;
            _files = files;
            _dialog = dialog;
            _session = session;
            _log = log;

            VerifyCommand = new RelayCommand(Verify, () => CanEditCritical && !IsVerified && IsAvailable);
            RefreshCommand = new RelayCommand(RefreshAll, () => IsVerified);
            ExportCommand = new RelayCommand(Export, () => IsVerified);
            CancelCommand = new RelayCommand(Cancel, () => IsVerified);
            UploadCommand = new AsyncRelayCommand(UploadAsync, () => IsVerified && !IsEditingPaths);
            EditPathsCommand = new RelayCommand(() => IsEditingPaths = true, () => IsVerified && !IsEditingPaths);
            SavePathsCommand = new RelayCommand(SavePaths, () => IsVerified && IsEditingPaths);
            BrowseCommand = new RelayCommand<HashKeyRow>(Browse, row => IsEditingPaths && row != null);
            ClearCommand = new RelayCommand<HashKeyRow>(Clear, row => IsEditingPaths && row != null && row.IsConfigured);

            Load();
        }

        public override string Title => "Hash Key";

        public bool IsAvailable => _hashes != null;

        public ObservableCollection<HashKeyRow> Rows { get; } = new ObservableCollection<HashKeyRow>();

        /// <summary>The rows on screen: every key, or only those with a file.</summary>
        public IEnumerable<HashKeyRow> VisibleRows => ShowAllKeys ? Rows : Rows.Where(r => r.IsConfigured);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(VisibleRows))]
        private bool _showAllKeys;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(VerifiedBy))]
        [NotifyCanExecuteChangedFor(nameof(VerifyCommand), nameof(RefreshCommand), nameof(ExportCommand), nameof(CancelCommand), nameof(UploadCommand), nameof(EditPathsCommand), nameof(SavePathsCommand))]
        private bool _isVerified;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(UploadCommand), nameof(EditPathsCommand), nameof(SavePathsCommand))]
        private bool _isEditingPaths;

        [ObservableProperty]
        private string _status = string.Empty;

        public string VerifiedBy => IsVerified && _approver != null ? "Two-step verification by " + _approver.Name + "." : string.Empty;

        public string MesNote => _mes != null && _mes.IsConfigured
            ? "Upload to MES refreshes MES's copy of this station's hash keys (SAVE_AUDIO_INFO)."
            : "MES is not configured on this machine (MesUrl in App.config), so the keys cannot be uploaded.";

        public IRelayCommand VerifyCommand { get; }
        public IRelayCommand RefreshCommand { get; }
        public IRelayCommand ExportCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IAsyncRelayCommand UploadCommand { get; }
        public IRelayCommand EditPathsCommand { get; }
        public IRelayCommand SavePathsCommand { get; }
        public IRelayCommand<HashKeyRow> BrowseCommand { get; }
        public IRelayCommand<HashKeyRow> ClearCommand { get; }

        partial void OnIsEditingPathsChanged(bool value)
        {
            BrowseCommand.NotifyCanExecuteChanged();
            ClearCommand.NotifyCanExecuteChanged();
            ShowAllKeys = value || ShowAllKeys;
        }

        protected override void OnLevelChanged()
        {
            if (!CanEditCritical)
                EndVerification();

            VerifyCommand.NotifyCanExecuteChanged();
        }

        public override void OnShown()
        {
            if (!IsEditingPaths)
                Load();
        }

        public override void OnHidden()
        {
            EndVerification();
        }

        private void Load()
        {
            Rows.Clear();
            if (_hashes == null)
            {
                Status = "Hash keys are not available on this machine.";
                OnPropertyChanged(nameof(VisibleRows));
                return;
            }

            IReadOnlyDictionary<string, string> paths = _hashes.Paths;
            Dictionary<string, HashKeyReading> latest = _hashes.ReadAll().ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
            foreach (HashKeyDefinition definition in _hashes.Definitions)
            {
                string path;
                paths.TryGetValue(definition.Key, out path);
                var row = new HashKeyRow(definition, path);
                HashKeyReading reading;
                latest.TryGetValue(definition.Key, out reading);
                row.Apply(reading);
                Rows.Add(row);
            }

            int configured = Rows.Count(r => r.IsConfigured);
            int failed = Rows.Count(r => r.HasError);
            Status = configured + " of " + Rows.Count + " keys have a file" + (failed > 0 ? "; " + failed + " could not be read." : ".");
            OnPropertyChanged(nameof(VisibleRows));
        }

        private void Verify()
        {
            AccessCheckResult approver = _badges != null ? _badges.RequestSecondApproval("PD-15 Uploaded Hash Keys to MES") : null;
            if (approver == null)
                return;

            _approver = approver;
            IsVerified = true;
            Record("two-step verification for the hash key page by " + approver.Name);
        }

        private void RefreshAll()
        {
            Load();
            Record("refreshed all hash keys");
        }

        private void Export()
        {
            if (_hashes == null || _files == null)
                return;

            string path = _files.SaveFile("Export all hash keys", "CSV files (*.csv)|*.csv",
                "HashKeys_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
            if (path == null)
                return;

            string error;
            if (!_hashes.ExportCsv(path, out error))
            {
                _dialog?.ShowError("The hash keys could not be exported: " + error, "Export all hash keys");
                return;
            }

            Record("exported all hash keys to " + path);
        }

        /// <summary>"Cancel all unsaved settings changes, that is: reload the settings that have taken effect."</summary>
        private void Cancel()
        {
            IsEditingPaths = false;
            Load();
        }

        private void Browse(HashKeyRow row)
        {
            if (row == null || _files == null)
                return;

            string path = _files.OpenFile("File for " + row.Key + " (" + row.Controls + ")", "All files (*.*)|*.*");
            if (path != null)
                row.FilePath = path;
        }

        private void Clear(HashKeyRow row)
        {
            if (row != null)
                row.FilePath = string.Empty;
            ClearCommand.NotifyCanExecuteChanged();
        }

        private void SavePaths()
        {
            if (_hashes == null)
                return;

            Dictionary<string, string> paths = Rows
                .Where(r => r.IsConfigured)
                .ToDictionary(r => r.Key, r => r.FilePath.Trim(), StringComparer.OrdinalIgnoreCase);

            string error;
            if (!_hashes.SavePaths(paths, out error))
            {
                _dialog?.ShowError(error, "File Path Setting");
                return;
            }

            Record("saved the hash key file paths (" + paths.Count + " keys)");
            IsEditingPaths = false;
            Load();
        }

        private async Task UploadAsync()
        {
            if (_hashes == null || _approver == null)
                return;

            Status = "Uploading to MES...";
            MesResult result = await _hashes.UploadToMesAsync(_approver.CardUid);
            Status = string.Empty;

            string response = "{\"Result\":" + (result.Ok ? "true" : "false") + ",\"RetMsg\":\"" + result.Message + "\"}";
            Record("uploaded the hash keys to MES: " + response);

            if (!result.Reached || !result.Ok)
            {
                _dialog?.ShowError("MES Response\n\n" + (result.Reached ? response : result.Message), "Upload to MES");
                Load();
                return;
            }

            _dialog?.ShowInfo("MES Response\n\n" + response, "Upload to MES");

            // After the upload the buttons are hidden and the authorized user is logged out.
            EndVerification();
            if (_session != null && _session.IsLoggedIn)
            {
                if (_log != null)
                    _log.Logout(_session.UserName, (int)_session.Level, "automatic - after uploading the hash keys to MES");
                _session.Logout();
            }
        }

        private void EndVerification()
        {
            _approver = null;
            IsEditingPaths = false;
            IsVerified = false;
        }

        private void Record(string action)
        {
            if (_log != null && _session != null)
                _log.Action(_session.UserName, (int)_session.Level, action);
        }
    }
}
