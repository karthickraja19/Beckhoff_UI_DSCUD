using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Recipe
{
    /// <summary>
    /// "Light Control" tab of the Recipe screen (V3 RecipieForm light-control side).
    /// Illumination intensities per colour code for vision-system calibration,
    /// with Edit/Save. Rows are seeded from the reference LightControl_Param.csv;
    /// in production they load from the recipe repository.
    /// </summary>
    public partial class LightRecipeViewModel : ObservableObject, Nova.ViewModels.Common.IEditableTab
    {
        private readonly IRecipeService _recipeService;
        private readonly IDialogService _dialogService;
        private readonly List<DynamicGridRow> _allRows = new List<DynamicGridRow>();

        public string Title => "Light Control";

        public ObservableCollection<DynamicColumnSchema> Columns { get; }
        public ObservableCollection<DynamicGridRow> Rows { get; } = new ObservableCollection<DynamicGridRow>();

        [ObservableProperty]
        private DynamicGridRow _selectedRow;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        [NotifyPropertyChangedFor(nameof(IsReadOnlyMode))]
        private bool _isEditing;

        public bool CanEdit => !IsEditing;
        public bool IsReadOnlyMode => !IsEditing;

        // ---- Filter ----
        // The button used to filter by the selected row's ColourCode. Every row in the
        // shipped table has the same colour code, so it always matched everything and
        // the table never visibly changed — the operator's reading of that is "the
        // filter does nothing". It is now a text filter across every column, with the
        // matched count shown, so any filter you apply is visible in the result.

        /// <summary>Whether the filter box is showing. Toggled by the Filter button.</summary>
        [ObservableProperty]
        private bool _isFilterVisible;

        [ObservableProperty]
        private string _filterText = "";

        [ObservableProperty]
        private string _filterSummary = "";

        public LightRecipeViewModel(IRecipeService recipeService, IDialogService dialogService)
        {
            _recipeService = recipeService ?? throw new ArgumentNullException(nameof(recipeService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            Columns = new ObservableCollection<DynamicColumnSchema>();
            LoadData();
        }

        private void LoadData()
        {
            var doc = _recipeService.LoadLightRecipeDynamic();
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

            _allRows.Clear();
            if (doc?.Rows != null && doc.Columns != null)
            {
                foreach (var row in doc.Rows)
                {
                    _allRows.Add(new DynamicGridRow(row, doc.Columns));
                }
            }

            ApplyFilter();
        }

        [RelayCommand]
        private void Edit() => IsEditing = true;

        [RelayCommand]
        private void Save()
        {
            // Validate every row, not just the visible ones: a filter must never let an
            // invalid value slip through because it happened to be scrolled out of view.
            if (_allRows.Any(r => r.HasErrors))
            {
                _dialogService.ShowError("Please correct invalid values before saving.", "Recipe");
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
                Rows = _allRows.Select(r => r.ToDictionary()).ToList(),
            };

            var result = _recipeService.SaveLightRecipeDynamic(doc);
            _dialogService.ShowInfo(result.Message, "Recipe");
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

        /// <summary>Shows or hides the filter box. Hiding it also drops the filter, so
        /// the table is never left narrowed by a control that is no longer on screen.</summary>
        [RelayCommand]
        private void Filter()
        {
            IsFilterVisible = !IsFilterVisible;
            if (!IsFilterVisible && !string.IsNullOrEmpty(FilterText))
            {
                FilterText = "";
            }
        }

        [RelayCommand]
        private void ClearFilter() => FilterText = "";

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        private void ApplyFilter()
        {
            string term = (FilterText ?? string.Empty).Trim();

            IEnumerable<DynamicGridRow> matched = _allRows;
            if (term.Length > 0)
            {
                matched = _allRows.Where(r => MatchesAnyColumn(r, term));
            }

            var visible = matched.ToList();

            Rows.Clear();
            foreach (var row in visible)
            {
                Rows.Add(row);
            }

            FilterSummary = term.Length == 0
                ? _allRows.Count + (_allRows.Count == 1 ? " row" : " rows")
                : "Showing " + visible.Count + " of " + _allRows.Count;

            SelectedRow = Rows.Count > 0 ? Rows[0] : null;
        }

        /// <summary>Matches the term against every column, so the operator can filter on
        /// a colour, a colour code or an intensity without picking a field first.</summary>
        private bool MatchesAnyColumn(DynamicGridRow row, string term)
        {
            foreach (var column in Columns)
            {
                string value = row[column.Key];
                if (!string.IsNullOrEmpty(value) &&
                    value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
