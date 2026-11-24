using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>
    /// An AE-limits parameter table (Production / CPK / GRR sub-tab of SFC &amp; PDCA
    /// Settings): searchable + paginated grid of <see cref="AeLimitRow"/> with Edit/Save.
    /// </summary>
    public partial class AeLimitsViewModel : ObservableObject, Nova.ViewModels.Common.IEditableTab
    {
        private readonly IMachineSettingsService _settingsService;
        private readonly IDialogService _dialogService;
        private readonly ICsvExportService _csv;
        private readonly AeLimitKind _kind;
        private readonly List<Nova.ViewModels.Pages.Recipe.DynamicGridRow> _all =
            new List<Nova.ViewModels.Pages.Recipe.DynamicGridRow>();

        public string Title { get; }

        public ObservableCollection<DynamicColumnSchema> Columns { get; } = new ObservableCollection<DynamicColumnSchema>();
        public ObservableCollection<Nova.ViewModels.Pages.Recipe.DynamicGridRow> Rows { get; } = new ObservableCollection<Nova.ViewModels.Pages.Recipe.DynamicGridRow>();

        [ObservableProperty]
        private string _searchText = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        [NotifyPropertyChangedFor(nameof(IsReadOnlyMode))]
        private bool _isEditing;

        [ObservableProperty]
        private string _rangeText;

        public bool CanEdit => !IsEditing;
        public bool IsReadOnlyMode => !IsEditing;

        // ---- Filter ----
        // Search matches the Parameter column only. Filter adds the column-value pass
        // the toolbar button always implied but never had: it was an empty TODO stub, so
        // pressing it did nothing at all.

        /// <summary>Whether the filter row is showing. Toggled by the Filter button.</summary>
        [ObservableProperty]
        private bool _isFilterVisible;

        /// <summary>Limits the filter to one column; empty means every column.</summary>
        [ObservableProperty]
        private string _filterColumn = AllColumns;

        [ObservableProperty]
        private string _filterText = "";

        public ObservableCollection<string> FilterColumns { get; } = new ObservableCollection<string>();

        private const string AllColumns = "All columns";

        public AeLimitsViewModel(
            string title,
            AeLimitKind kind,
            IMachineSettingsService settingsService,
            IDialogService dialogService,
            ICsvExportService csv = null)
        {
            Title = title;
            _kind = kind;
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _csv = csv;

            LoadData();
        }

        private void LoadData()
        {
            var doc = _settingsService.LoadAeLimitsDynamic(_kind);
            if (doc != null && doc.HasMissingColumnSchema && !string.IsNullOrWhiteSpace(doc.MissingSchemaMessage))
            {
                _dialogService.ShowWarning(doc.MissingSchemaMessage, "Column Schema Warning");
            }

            Columns.Clear();
            if (doc?.Columns != null)
            {
                foreach (var col in doc.Columns)
                {
                    Columns.Add(col);
                }
            }

            FilterColumns.Clear();
            FilterColumns.Add(AllColumns);
            foreach (var col in Columns)
            {
                FilterColumns.Add(col.Title ?? col.Key);
            }

            if (!FilterColumns.Contains(FilterColumn))
            {
                FilterColumn = AllColumns;
            }

            _all.Clear();
            if (doc?.Rows != null && doc.Columns != null)
            {
                foreach (var row in doc.Rows)
                {
                    _all.Add(new Nova.ViewModels.Pages.Recipe.DynamicGridRow(row, doc.Columns));
                }
            }

            Refresh();
        }

        partial void OnSearchTextChanged(string value) => Refresh();

        partial void OnFilterTextChanged(string value) => Refresh();

        partial void OnFilterColumnChanged(string value) => Refresh();

        private IEnumerable<Nova.ViewModels.Pages.Recipe.DynamicGridRow> Filtered()
        {
            IEnumerable<Nova.ViewModels.Pages.Recipe.DynamicGridRow> rows = _all;

            string search = (SearchText ?? string.Empty).Trim();
            if (search.Length > 0)
            {
                rows = rows.Where(r => Contains(r["Parameter"], search));
            }

            string filter = (FilterText ?? string.Empty).Trim();
            if (filter.Length > 0)
            {
                rows = rows.Where(r => MatchesFilter(r, filter));
            }

            return rows;
        }

        private bool MatchesFilter(Nova.ViewModels.Pages.Recipe.DynamicGridRow row, string term)
        {
            // A named column narrows to that column; "All columns" scans every one, so
            // the operator can filter on a unit or a limit value without choosing first.
            if (!string.IsNullOrEmpty(FilterColumn) && FilterColumn != AllColumns)
            {
                var column = Columns.FirstOrDefault(
                    c => string.Equals(c.Title ?? c.Key, FilterColumn, StringComparison.OrdinalIgnoreCase));
                if (column != null)
                {
                    return Contains(row[column.Key], term);
                }
            }

            foreach (var column in Columns)
            {
                if (Contains(row[column.Key], term))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(string value, string term)
            => !string.IsNullOrEmpty(value) && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

        private void Refresh()
        {
            var filtered = Filtered().ToList();
            int count = filtered.Count;

            Rows.Clear();
            foreach (var row in filtered)
                Rows.Add(row);

            // Say what was hidden as well as what is shown — a table narrowed to nothing
            // otherwise looks like a table that failed to load.
            RangeText = count == _all.Count
                ? (count == 1 ? "1 parameter" : count + " parameters")
                : "Showing " + count + " of " + _all.Count + " parameters";
        }

        [RelayCommand]
        private void ClearSearch() => SearchText = "";

        [RelayCommand]
        private void ClearFilter() => FilterText = "";

        [RelayCommand]
        private void Edit() => IsEditing = true;

        [RelayCommand]
        private void Save()
        {
            // Validate every row, not just the visible ones: a filter must never let an
            // invalid value through because it happened to be filtered out of view.
            if (_all.Any(r => r.HasErrors))
            {
                _dialogService.ShowError("Please correct invalid values before saving.", "Settings");
                return;
            }

            if (!_dialogService.Confirm("Do you want to save the data?", "Confirmation"))
            {
                return;
            }

            var doc = new DynamicTableDocument
            {
                Title = Title,
                Columns = Columns.ToList(),
                Rows = _all.Select(r => r.ToDictionary()).ToList(),
            };

            var result = _settingsService.SaveAeLimitsDynamic(_kind, doc);
            _dialogService.ShowInfo(result.Message, "Settings");
            if (result.IsSaved || result.IsNoChanges)
            {
                IsEditing = false;
            }
        }

        /// <summary>Leaving the tab abandons the edit: relock and reload from the store.</summary>
        public void CancelEdit()
        {
            if (!IsEditing)
            {
                return;
            }

            IsEditing = false;
            LoadData();
        }

        /// <summary>Shows or hides the filter row. Hiding it drops the filter, so the
        /// table is never left narrowed by a control that is no longer on screen.</summary>
        [RelayCommand]
        private void Filter()
        {
            IsFilterVisible = !IsFilterVisible;
            if (!IsFilterVisible && !string.IsNullOrEmpty(FilterText))
            {
                FilterText = "";
            }
        }

        /// <summary>
        /// Writes what is on screen to CSV — search and filter included, so the export
        /// matches the table the operator is looking at. CSV is the only export format
        /// anywhere in V3 (every Save button there is a SaveFileDialog over writeCSV).
        /// </summary>
        [RelayCommand]
        private void Export()
        {
            if (_csv == null)
            {
                _dialogService.ShowError("Export is not available: no export service is configured.", "Settings");
                return;
            }

            var visible = Filtered().ToList();
            if (visible.Count == 0)
            {
                _dialogService.ShowInfo("There is nothing to export.", "Settings");
                return;
            }

            var headers = Columns.Select(c => c.Title ?? c.Key).ToList();
            var rows = visible
                .Select(r => (IReadOnlyList<string>)Columns.Select(c => r[c.Key] ?? string.Empty).ToList())
                .ToList();

            _csv.Export(headers, rows,
                Title.Replace(" ", "_").Replace("(", "").Replace(")", "")
                + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv");
        }
    }
}
