using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Recipe
{
    /// <summary>
    /// "Robot" tab of the Recipe screen (V3 RecipieForm robot side, dgv_Robot).
    /// Dynamic point-parameter table driven directly from JSON schema and keys.
    /// Supports arbitrary columns, inline editing, numeric validation, and dynamic audit logging.
    /// Save also writes the rows mapped to a PLC member (Speed, ...) to the PLC's robots (IPlcRecipeService).
    /// </summary>
    public partial class RobotRecipeViewModel : ObservableObject, Nova.ViewModels.Common.IEditableTab
    {
        private readonly IRecipeService _recipeService;
        private readonly IDialogService _dialogService;
        private readonly IPlcRecipeService _plcRecipeService;

        public string Title { get; private set; } = "Robot";

        public ObservableCollection<DynamicColumnSchema> Columns { get; } = new ObservableCollection<DynamicColumnSchema>();
        public ObservableCollection<DynamicGridRow> Rows { get; } = new ObservableCollection<DynamicGridRow>();

        [ObservableProperty]
        private DynamicGridRow _selectedRow;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        [NotifyPropertyChangedFor(nameof(IsReadOnlyMode))]
        private bool _isEditing;

        public bool CanEdit => !IsEditing;
        public bool IsReadOnlyMode => !IsEditing;

        public RobotRecipeViewModel(IRecipeService recipeService, IDialogService dialogService, IPlcRecipeService plcRecipeService)
        {
            _recipeService = recipeService ?? throw new ArgumentNullException(nameof(recipeService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _plcRecipeService = plcRecipeService ?? throw new ArgumentNullException(nameof(plcRecipeService));

            LoadData();
        }

        public void LoadData()
        {
            var doc = _recipeService.LoadRobotRecipeDynamic();
            if (doc != null && doc.HasMissingColumnSchema && !string.IsNullOrWhiteSpace(doc.MissingSchemaMessage))
            {
                _dialogService.ShowWarning(doc.MissingSchemaMessage, "Column Schema Warning");
            }

            if (doc != null && !string.IsNullOrWhiteSpace(doc.Title))
            {
                Title = doc.Title;
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
                foreach (var rowDict in doc.Rows)
                {
                    Rows.Add(new DynamicGridRow(rowDict, doc.Columns));
                }
            }

            if (Rows.Count > 0)
            {
                SelectedRow = Rows[0];
            }
        }

        [RelayCommand]
        private void Edit() => IsEditing = true;

        [RelayCommand]
        private async Task Save()
        {
            if (Rows.Any(r => r.HasErrors))
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
                Rows = Rows.Select(r => r.ToDictionary()).ToList()
            };

            var result = _recipeService.SaveRobotRecipeDynamic(doc);
            if (!result.IsSaved && !result.IsNoChanges)
            {
                _dialogService.ShowInfo(result.Message, "Recipe");
                return;
            }

            IsEditing = false;

            // The PLC does not keep the robot values, so they are written on every Save - also
            // when nothing changed, which resends values the PLC lost.
            try
            {
                await _plcRecipeService.WriteRobotRecipeAsync();
                _dialogService.ShowInfo(result.Message + "\nRobot recipe written to the PLC.", "Recipe");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError(result.Message + "\nNot written to the PLC: " + ex.Message, "Recipe");
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
    }
}
