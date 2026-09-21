using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>
    /// "Vision Settings" tab — the colour-parameter table (V3 Table_Vision_Settings,
    /// persisted in Vision_Settings.xml): Colour / OPT ID / Camera ID rows with one
    /// ACTIVE recipe, plus New Parameter and Edit/Save.
    /// </summary>
    public partial class VisionSettingsViewModel : ObservableObject, Nova.ViewModels.Common.IEditableTab
    {
        private readonly IMachineSettingsService _settingsService;
        private readonly IDialogService _dialogService;

        public string Title => "Vision Settings";

        public ObservableCollection<DynamicColumnSchema> Columns { get; } = new ObservableCollection<DynamicColumnSchema>();
        public ObservableCollection<Nova.ViewModels.Pages.Recipe.DynamicGridRow> Rows { get; } = new ObservableCollection<Nova.ViewModels.Pages.Recipe.DynamicGridRow>();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        [NotifyPropertyChangedFor(nameof(IsReadOnlyMode))]
        private bool _isEditing;

        public bool CanEdit => !IsEditing;
        public bool IsReadOnlyMode => !IsEditing;

        public VisionSettingsViewModel(IMachineSettingsService settingsService, IDialogService dialogService)
        {
            _settingsService = settingsService ?? throw new System.ArgumentNullException(nameof(settingsService));
            _dialogService = dialogService ?? throw new System.ArgumentNullException(nameof(dialogService));

            LoadData();
        }

        private void LoadData()
        {
            var doc = _settingsService.LoadVisionSettingsDynamic();
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

            Rows.Clear();
            if (doc?.Rows != null && doc.Columns != null)
            {
                foreach (var row in doc.Rows)
                {
                    Rows.Add(new Nova.ViewModels.Pages.Recipe.DynamicGridRow(row, doc.Columns));
                }
            }
        }

        [RelayCommand]
        private void NewParameter()
        {
            var dict = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var col in Columns)
            {
                dict[col.Key] = string.Empty;
            }

            // Ensure fallback standard keys exist even if Columns is not yet populated
            if (!dict.ContainsKey("Colour")) dict["Colour"] = string.Empty;
            if (!dict.ContainsKey("OptId")) dict["OptId"] = string.Empty;
            if (!dict.ContainsKey("CameraId")) dict["CameraId"] = string.Empty;

            Rows.Add(new Nova.ViewModels.Pages.Recipe.DynamicGridRow(dict, Columns));
            IsEditing = true;
        }

        [RelayCommand]
        private void Edit() => IsEditing = true;

        [RelayCommand]
        private void Save()
        {
            if (Rows.Count == 0)
            {
                _dialogService.ShowError("Vision settings table is empty.", "Settings");
                return;
            }

            if (Rows.Any(r => r.HasErrors))
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
                Rows = Rows.Select(r => r.ToDictionary()).ToList(),
            };

            var result = _settingsService.SaveVisionSettingsDynamic(doc);
            _dialogService.ShowInfo(result.Message, "Settings");

            // "No data modified !" ends the edit too: the operator pressed Save and
            // there was nothing to write, so the grid should relock either way.
            if (result.IsSaved || result.IsNoChanges)
            {
                IsEditing = false;
            }
        }

        /// <summary>Leaving the tab abandons the edit — including any row added by New
        /// Parameter and never saved — by relocking and reloading from the store.</summary>
        public void CancelEdit()
        {
            if (!IsEditing)
            {
                return;
            }

            IsEditing = false;
            LoadData();
        }
    }
}
