using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>
    /// The axis table: the servos of Data\Servos.json (the same entries the Recipe page's
    /// Gantry tab teaches positions into), with Edit/Save writing back into that file.
    /// </summary>
    public partial class AxisConfigurationTableViewModel : ObservableObject, IEditableTab
    {
        private readonly IMachineSettingsService _settingsService;
        private readonly IDialogService _dialogService;

        public string Title => "Axis Configuration";

        public ObservableCollection<DynamicColumnSchema> Columns { get; } = new ObservableCollection<DynamicColumnSchema>();
        public ObservableCollection<Nova.ViewModels.Pages.Recipe.DynamicGridRow> Rows { get; } = new ObservableCollection<Nova.ViewModels.Pages.Recipe.DynamicGridRow>();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        [NotifyPropertyChangedFor(nameof(IsReadOnlyMode))]
        private bool _isEditing;

        public bool CanEdit => !IsEditing;
        public bool IsReadOnlyMode => !IsEditing;

        public AxisConfigurationTableViewModel(IMachineSettingsService settingsService, IDialogService dialogService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            LoadData();
        }

        private void LoadData()
        {
            var doc = _settingsService.LoadAxisParametersDynamic();
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
        private void Edit()
        {
            // Servos.json may have changed since the table was loaded (a Recipe save writes
            // it too), so edit what is in the file now.
            LoadData();
            IsEditing = true;
        }

        [RelayCommand]
        private void Save()
        {
            if (Rows.Count == 0)
            {
                _dialogService.ShowError("Axis parameter table is empty.", "Settings");
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

            RecipeSaveResult result;
            try
            {
                result = _settingsService.SaveAxisParametersDynamic(doc);
            }
            catch (InvalidOperationException ex)
            {
                _dialogService.ShowError(ex.Message, "Settings");
                return;
            }

            _dialogService.ShowInfo(result.Message, "Settings");

            // "No data modified !" ends the edit too: the operator pressed Save and
            // there was nothing to write, so the grid should relock either way.
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
    }

    /// <summary>
    /// "Axis Parameter Settings" tab — hosts three sub-tabs: the axis-parameter table,
    /// the TCP/IP connection grid, and the Serial Comm connection grid.
    /// </summary>
    public partial class AxisConfigurationViewModel : ObservableObject, ITabHost
    {
        public string Title => "Axis Configuration Settings";

        public AxisConfigurationTableViewModel Table { get; }
        public ConnectionGridViewModel TcpCcd { get; }
        public ConnectionGridViewModel TcpRobot { get; }
        public ConnectionGridViewModel TcpScanner { get; }
        public ConnectionGridViewModel TcpSfcPdca { get; }
        public ConnectionGridViewModel Serial { get; }

        public ObservableCollection<TabItemViewModel> Tabs { get; }

        [ObservableProperty]
        private TabItemViewModel _selectedTab;

        public AxisConfigurationViewModel(IMachineSettingsService settingsService, IDialogService dialogService)
        {
            if (settingsService == null) throw new ArgumentNullException(nameof(settingsService));
            if (dialogService == null) throw new ArgumentNullException(nameof(dialogService));

            Table = new AxisConfigurationTableViewModel(settingsService, dialogService);
            TcpCcd = new ConnectionGridViewModel(
                "Name",
                "IP Address",
                "Port",
                settingsService,
                dialogService,
                ConnectionGridKind.TcpCcd);
            TcpRobot = new ConnectionGridViewModel(
                "Name",
                "IP Address",
                "Port",
                settingsService,
                dialogService,
                ConnectionGridKind.TcpRobot);
            TcpScanner = new ConnectionGridViewModel(
                "Name",
                "IP Address",
                "Port",
                settingsService,
                dialogService,
                ConnectionGridKind.TcpScanner);
            TcpSfcPdca = new ConnectionGridViewModel(
                "Name",
                "IP Address",
                "Port",
                settingsService,
                dialogService,
                ConnectionGridKind.TcpSfcPdca);
            Serial = new ConnectionGridViewModel(
                "COM Port",
                "Baud Rate",
                "Data Bits",
                settingsService,
                dialogService,
                ConnectionGridKind.Serial);

            Tabs = new ObservableCollection<TabItemViewModel>
            {
                new TabItemViewModel("Axis Configuration", "Move", Table),
                new TabItemViewModel("CCD", "Network", TcpCcd),
                new TabItemViewModel("Robot", "Network", TcpRobot),
                new TabItemViewModel("Scanner", "Network", TcpScanner),
                new TabItemViewModel("SFC PDCA", "Network", TcpSfcPdca),
                new TabItemViewModel("Serial Comm", "Cable", Serial),
            };
            _selectedTab = Tabs[0];
        }

        /// <summary>Switching sub-tabs abandons an unsaved edit on the one being left.</summary>
        partial void OnSelectedTabChanged(TabItemViewModel oldValue, TabItemViewModel newValue)
        {
            oldValue?.CancelEdits();
        }
    }
}
