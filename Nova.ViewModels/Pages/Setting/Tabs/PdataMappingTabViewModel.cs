using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The PDATA Mapping tab of the Setting page: pick PDATA fields on the left, set their
    /// limits in the grid on the right, and save the result as a machine profile
    /// (PDATA\{machine}.json). The query template written beside it
    /// (PDATA\{machine}_template.json) is what a reported unit is resolved against.
    /// </summary>
    /// <remarks>
    /// Level 3 only (Setting page). The grid is locked until Edit is pressed, and leaving the
    /// tab abandons an unsaved edit.
    /// </remarks>
    public partial class PdataMappingTabViewModel : SettingsTabBase
    {
        private readonly IPdataMappingService _service;
        private readonly IDialogService _dialog;
        private readonly IAppSession _session;
        private readonly IAccessLog _log;

        /// <summary>Every field, unfiltered - the picker shows a search-narrowed view of this.</summary>
        private readonly List<PdataFieldItem> _allFields = new List<PdataFieldItem>();

        /// <summary>Guards the rebuild path so programmatic ticks do not re-enter the grid sync.</summary>
        private bool _suppressSelectionSync;

        public PdataMappingTabViewModel(IPdataMappingService service, IDialogService dialog, IAppSession session, IAccessLog log)
        {
            _service = service;
            _dialog = dialog;
            _session = session;
            _log = log;

            EditCommand = new RelayCommand(Edit, () => CanEditCritical && IsAvailable && !IsEditing);
            SaveCommand = new RelayCommand(Save, () => IsEditing);
            CancelCommand = new RelayCommand(CancelEdit, () => IsEditing);
            PreviewCommand = new RelayCommand(Preview, () => IsAvailable);
            ClearAllCommand = new RelayCommand(ClearAll, () => IsEditing);
            ImportCsvCommand = new RelayCommand(ImportCsv, () => IsEditing);
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);

            LoadFields();
            LoadMachines();
        }

        public override string Title => "PDATA Mapping";

        public bool IsAvailable => _service != null;

        public ObservableCollection<PdataFieldItem> Fields { get; } = new ObservableCollection<PdataFieldItem>();
        public ObservableCollection<PdataRow> Rows { get; } = new ObservableCollection<PdataRow>();
        public ObservableCollection<string> Machines { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _selectedMachine;

        /// <summary>Blanks Min/Max in the template.</summary>
        [ObservableProperty]
        private bool _ignoreLimits;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsReadOnly), nameof(AccessHint))]
        private bool _isEditing;

        [ObservableProperty]
        private string _rangeText;

        public bool IsReadOnly => !IsEditing;

        public string AccessHint => !IsAvailable
            ? "The PDATA mapping store is not available."
            : IsEditing
                ? "Tick the fields to report, set their limits, then Save."
                : CanEditCritical
                    ? "Press Edit to change the mapping."
                    : "Read only. Changing the PDATA mapping needs Level 3.";

        public IRelayCommand EditCommand { get; }
        public IRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand PreviewCommand { get; }
        public IRelayCommand ClearAllCommand { get; }
        public IRelayCommand ImportCsvCommand { get; }
        public IRelayCommand ClearSearchCommand { get; }

        partial void OnIsEditingChanged(bool value)
        {
            EditCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
            ClearAllCommand.NotifyCanExecuteChanged();
            ImportCsvCommand.NotifyCanExecuteChanged();
        }

        protected override void OnLevelChanged()
        {
            if (!CanEditCritical && IsEditing)
                CancelEdit();

            OnPropertyChanged(nameof(AccessHint));
            EditCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Coming back to the tab picks up a profile saved elsewhere.</summary>
        public override void OnShown()
        {
            if (!IsEditing && !string.IsNullOrWhiteSpace(SelectedMachine))
                LoadMachineConfig(SelectedMachine);
        }

        /// <summary>Leaving the tab abandons the edit: relock and reload from the store.</summary>
        public override void OnHidden()
        {
            CancelEdit();
        }

        // ---------------------------------------------------------------- loading

        private void LoadFields()
        {
            _allFields.Clear();
            foreach (string name in _service?.GetAvailableFields() ?? new List<string>())
            {
                var item = new PdataFieldItem(name);
                item.SelectionChanged += OnFieldSelectionChanged;
                _allFields.Add(item);
            }
            ApplySearch();
        }

        private void LoadMachines()
        {
            Machines.Clear();
            foreach (string name in _service?.GetMachineNames() ?? new List<string>())
                Machines.Add(name);

            if (SelectedMachine == null && Machines.Count > 0)
                SelectedMachine = Machines[0];
        }

        partial void OnSearchTextChanged(string value)
        {
            ApplySearch();
        }

        private void ApplySearch()
        {
            string term = (SearchText ?? string.Empty).Trim();
            List<PdataFieldItem> visible = string.IsNullOrEmpty(term)
                ? _allFields
                : _allFields.Where(f => f.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            Fields.Clear();
            foreach (PdataFieldItem f in visible)
                Fields.Add(f);

            int total = _allFields.Count;
            RangeText = visible.Count == total
                ? (total == 1 ? "1 field" : total + " fields")
                : "Showing " + visible.Count + " of " + total + " fields";
        }

        partial void OnSelectedMachineChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || IsEditing)
                return;

            LoadMachineConfig(value);
        }

        private void OnFieldSelectionChanged(object sender, EventArgs e)
        {
            if (!_suppressSelectionSync)
                SyncGridFromSelection();
        }

        /// <summary>
        /// Rebuilds the grid to match the ticked fields, keeping anything already typed
        /// against a field that is still selected.
        /// </summary>
        private void SyncGridFromSelection()
        {
            Dictionary<string, PdataRow> existing = Rows
                .GroupBy(r => r.Placeholder, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            Rows.Clear();
            foreach (PdataFieldItem field in _allFields.Where(f => f.IsSelected))
            {
                PdataRow kept;
                Rows.Add(existing.TryGetValue(field.Name, out kept) ? kept : NewRow(field.Name, null));
            }
        }

        /// <summary>
        /// SN-type attributes carry no limits and have a shorter query line; the name says
        /// which: a "sn" word in it (unit_sn, front_glue_sn, Pallet_SN).
        /// </summary>
        public static bool IsSnField(string fieldName)
        {
            return (fieldName ?? string.Empty)
                .Split(new[] { '_', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(token => string.Equals(token, "sn", StringComparison.OrdinalIgnoreCase));
        }

        private static PdataRow NewRow(string fieldName, Dictionary<string, string> saved)
        {
            Func<string, string, string> value = (key, fallback) =>
            {
                string v;
                return saved != null && saved.TryGetValue(key, out v) && !string.IsNullOrEmpty(v) ? v : fallback;
            };

            return new PdataRow(
                fieldName,
                value("colDatatype", IsSnField(fieldName) ? "attr" : "pdata"),
                value("colPrefix", string.Empty),
                value("colSuffix", string.Empty),
                value("colMinLimit", string.Empty),
                value("colMaxLimit", string.Empty),
                value("colUnit", string.Empty));
        }

        private void LoadMachineConfig(string machineName)
        {
            PdataMachineConfig config = _service?.LoadMachineConfig(machineName);

            _suppressSelectionSync = true;
            try
            {
                var selected = new HashSet<string>(config?.SelectedFields ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                foreach (PdataFieldItem f in _allFields)
                    f.IsSelected = selected.Contains(f.Name);
            }
            finally
            {
                _suppressSelectionSync = false;
            }

            var savedRows = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, string> row in config?.Rows ?? new List<Dictionary<string, string>>())
            {
                string key;
                if (row.TryGetValue("colPlaceholder", out key) && !string.IsNullOrEmpty(key) && !savedRows.ContainsKey(key))
                    savedRows[key] = row;
            }

            Rows.Clear();
            foreach (PdataFieldItem field in _allFields.Where(f => f.IsSelected))
            {
                Dictionary<string, string> saved;
                savedRows.TryGetValue(field.Name, out saved);
                Rows.Add(NewRow(field.Name, saved));
            }

            IgnoreLimits = false;
        }

        // ---------------------------------------------------------------- query

        private PdataQueryTemplate BuildTemplate(string machineName)
        {
            return new PdataQueryTemplate
            {
                MachineName = machineName ?? string.Empty,
                GeneratedAt = DateTime.UtcNow.ToString("o"),
                IgnoreLimits = IgnoreLimits,
                Fields = Rows.Select(r => new PdataFieldConfig
                {
                    ParameterName = r.Placeholder.Trim(),
                    DataType = r.Datatype.Trim(),
                    MinLimit = IgnoreLimits ? string.Empty : (r.MinLimit ?? string.Empty).Trim(),
                    MaxLimit = IgnoreLimits ? string.Empty : (r.MaxLimit ?? string.Empty).Trim(),
                    Unit = (r.Unit ?? string.Empty).Trim(),
                    Prefix = r.Prefix.Trim(),
                    IsSNField = IsSnField(r.Placeholder),
                }).ToList()
            };
        }

        /// <summary>[SN] and [SUFFIX] stay as literal tokens: they are resolved when a unit is reported.</summary>
        private static IReadOnlyList<string> BuildQueryLines(PdataQueryTemplate template)
        {
            return template.Fields.Select(f => f.IsSNField
                ? "[SN]@" + f.DataType + "@" + f.ParameterName + "@[SUFFIX]@" + f.Unit
                : "[SN]@" + f.DataType + "@" + f.ParameterName + "@[SUFFIX]@" + f.MinLimit + "@" + f.MaxLimit + "@" + f.Unit)
                .ToList();
        }

        // ---------------------------------------------------------------- commands

        private void Edit()
        {
            IsEditing = true;
        }

        private void Preview()
        {
            if (Rows.Count == 0)
            {
                _dialog?.ShowInfo("No fields selected. Please tick PDATA items from the list first.", "No Fields");
                return;
            }

            _dialog?.ShowInfo(string.Join(Environment.NewLine, BuildQueryLines(BuildTemplate(SelectedMachine))), "Query Preview");
        }

        private void ClearAll()
        {
            if (_dialog != null && !_dialog.Confirm("Clear every selected PDATA field?", "Confirmation"))
                return;

            _suppressSelectionSync = true;
            try
            {
                foreach (PdataFieldItem f in _allFields)
                    f.IsSelected = false;
            }
            finally
            {
                _suppressSelectionSync = false;
            }

            Rows.Clear();
        }

        private void ImportCsv()
        {
            IReadOnlyList<string> imported = _service?.ImportFieldNamesFromCsv();
            if (imported == null)
                return;                                 // the operator cancelled the dialog

            if (imported.Count == 0)
            {
                _dialog?.ShowWarning("No PDATA names found in the CSV file.", "Empty Import");
                return;
            }

            var byName = new Dictionary<string, PdataFieldItem>(StringComparer.OrdinalIgnoreCase);
            foreach (PdataFieldItem f in _allFields)
                byName[f.Name] = f;

            int matched = 0;
            var notFound = new List<string>();

            _suppressSelectionSync = true;
            try
            {
                foreach (PdataFieldItem f in _allFields)
                    f.IsSelected = false;

                foreach (string name in imported)
                {
                    PdataFieldItem item;
                    if (byName.TryGetValue(name, out item))
                    {
                        item.IsSelected = true;
                        matched++;
                    }
                    else
                    {
                        notFound.Add(name);
                    }
                }
            }
            finally
            {
                _suppressSelectionSync = false;
            }

            SyncGridFromSelection();

            string message = "Imported " + matched + " PDATA item(s).";
            if (notFound.Count > 0)
            {
                message += Environment.NewLine + notFound.Count + " item(s) not found in the list:" + Environment.NewLine
                           + string.Join(", ", notFound.Take(10));
                if (notFound.Count > 10)
                    message += Environment.NewLine + "  ... and " + (notFound.Count - 10) + " more";
                _dialog?.ShowWarning(message, "CSV Import");
            }
            else
            {
                _dialog?.ShowInfo(message, "CSV Import");
            }
        }

        private void Save()
        {
            if (Rows.Any(r => r.HasErrors))
            {
                _dialog?.ShowError("Please correct invalid values before saving.", "PDATA Mapping");
                return;
            }

            string machineName = (SelectedMachine ?? string.Empty).Trim();
            if (machineName.Length == 0)
            {
                _dialog?.ShowError("Choose or type a machine name before saving.", "PDATA Mapping");
                return;
            }

            if (_dialog != null && !_dialog.Confirm("Do you want to save the data?", "Confirmation"))
                return;

            PdataQueryTemplate template = BuildTemplate(machineName);
            var config = new PdataMachineConfig
            {
                MachineName = machineName,
                SelectedFields = _allFields.Where(f => f.IsSelected).Select(f => f.Name).ToList(),
                Rows = Rows.Select(r => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["colPrefix"] = r.Prefix,
                    ["colDatatype"] = r.Datatype,
                    ["colPlaceholder"] = r.Placeholder,
                    ["colSuffix"] = r.Suffix,
                    ["colMinLimit"] = r.MinLimit ?? string.Empty,
                    ["colMaxLimit"] = r.MaxLimit ?? string.Empty,
                    ["colUnit"] = r.Unit ?? string.Empty,
                }).ToList(),
                Query = BuildQueryLines(template),
            };

            RecipeSaveResult result = _service.SaveMachineConfig(config);
            if (result == null)
                return;     // the write failed and the service has said so; stay unlocked so nothing typed is lost

            // The template is what is read back when a unit is reported, so it is rewritten
            // whenever the profile is - otherwise the next unit would use stale limits.
            if (result.IsSaved)
            {
                _service.SaveQueryTemplate(template);
                if (!Machines.Contains(machineName))
                    Machines.Add(machineName);

                _log?.Action(_session?.UserName, (int)(_session?.Level ?? AccessLevel.None),
                    "PDATA mapping '" + machineName + "' saved: " + config.SelectedFields.Count + " field(s)");
            }

            _dialog?.ShowInfo(result.Message, "PDATA Mapping");
            if (result.IsSaved || result.IsNoChanges)
                IsEditing = false;
        }

        /// <summary>Ends the edit and reloads the profile from the store, dropping what was typed.</summary>
        private void CancelEdit()
        {
            if (!IsEditing)
                return;

            IsEditing = false;
            if (!string.IsNullOrWhiteSpace(SelectedMachine))
                LoadMachineConfig(SelectedMachine);
        }
    }
}
