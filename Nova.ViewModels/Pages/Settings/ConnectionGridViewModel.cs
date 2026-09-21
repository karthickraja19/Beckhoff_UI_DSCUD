using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Settings
{
    public enum ConnectionGridKind
    {
        TcpCcd = 0,
        TcpRobot = 1,
        TcpScanner = 2,
        TcpSfcPdca = 3,
        Serial = 4,
    }

    /// <summary>
    /// A connection-details grid (one per TCP/IP or Serial Comm sub-tab): a table of
    /// <see cref="ConnectionRow"/> with Edit/Save. Cells are locked until Edit.
    /// </summary>
    public partial class ConnectionGridViewModel : ObservableObject, Nova.ViewModels.Common.IEditableTab
    {
        private readonly IMachineSettingsService _settingsService;
        private readonly IDialogService _dialogService;
        private readonly ConnectionGridKind _kind;
        private readonly Dictionary<string, SerialPortRecord> _serialRecordsByName =
            new Dictionary<string, SerialPortRecord>(System.StringComparer.OrdinalIgnoreCase);

        public string Title { get; }
        public string Icon { get; }
        public string Col1Header { get; }
        public string Col2Header { get; }
        public string Col3Header { get; }

        public ObservableCollection<DynamicColumnSchema> Columns { get; }
        public ObservableCollection<Nova.ViewModels.Pages.Recipe.DynamicGridRow> Rows { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        [NotifyPropertyChangedFor(nameof(IsReadOnlyMode))]
        private bool _isEditing;

        public bool CanEdit => !IsEditing;
        public bool IsReadOnlyMode => !IsEditing;

        public ConnectionGridViewModel(
            string col1Header,
            string col2Header,
            string col3Header,
            IMachineSettingsService settingsService,
            IDialogService dialogService,
            ConnectionGridKind kind)
        {
            Col1Header = col1Header;
            Col2Header = col2Header;
            Col3Header = col3Header;
            _settingsService = settingsService ?? throw new System.ArgumentNullException(nameof(settingsService));
            _dialogService = dialogService ?? throw new System.ArgumentNullException(nameof(dialogService));
            _kind = kind;

            switch (kind)
            {
                case ConnectionGridKind.TcpCcd:
                    Title = "CCD TCP Settings";
                    Icon = "Network";
                    break;
                case ConnectionGridKind.TcpRobot:
                    Title = "Robot TCP Settings";
                    Icon = "Network";
                    break;
                case ConnectionGridKind.TcpScanner:
                    Title = "Scanner TCP Settings";
                    Icon = "Network";
                    break;
                case ConnectionGridKind.TcpSfcPdca:
                    Title = "SFC PDCA TCP Settings";
                    Icon = "Network";
                    break;
                default:
                    Title = "Serial Comm Settings";
                    Icon = "Cable";
                    break;
            }

            Columns = new ObservableCollection<DynamicColumnSchema>();
            Rows = new ObservableCollection<Nova.ViewModels.Pages.Recipe.DynamicGridRow>();
            ReloadRows();
        }

        [RelayCommand]
        private void Edit() => IsEditing = true;

        [RelayCommand]
        private void Save()
        {
            if (Rows.Any(r => r.HasErrors))
            {
                _dialogService.ShowError("Please correct invalid values before saving.", "Settings");
                return;
            }

            var doc = new DynamicTableDocument
            {
                Title = Title,
                Columns = Columns.ToList(),
                Rows = Rows.Select(r => r.ToDictionary()).ToList(),
            };

            RecipeSaveResult result;
            switch (_kind)
            {
                case ConnectionGridKind.TcpCcd:
                    result = _settingsService.SaveTcpConnectionsDynamic("CCD", doc);
                    break;
                case ConnectionGridKind.TcpRobot:
                    result = _settingsService.SaveTcpConnectionsDynamic("Robot", doc);
                    break;
                case ConnectionGridKind.TcpScanner:
                    result = _settingsService.SaveTcpConnectionsDynamic("Scanner", doc);
                    break;
                case ConnectionGridKind.TcpSfcPdca:
                    result = _settingsService.SaveTcpConnectionsDynamic("SFC_PDCA", doc);
                    break;
                default:
                    result = _settingsService.SaveSerialPortsDynamic(doc);
                    break;
            }

            _dialogService.ShowInfo(result.Message, "Settings");

            // "No data modified !" ends the edit too: the operator pressed Save and
            // there was nothing to write, so the edit is finished either way. Leaving
            // IsEditing set is what kept the grid unlocked afterwards.
            if (result.IsSaved || result.IsNoChanges)
            {
                IsEditing = false;
            }
        }

        /// <summary>Leaving the tab abandons the edit: relock and reload, so the next
        /// visit shows what is actually on disk rather than half-typed cells.</summary>
        public void CancelEdit()
        {
            if (!IsEditing)
            {
                return;
            }

            IsEditing = false;
            ReloadRows();
        }

        private void ReloadRows()
        {
            Columns.Clear();
            Rows.Clear();

            DynamicTableDocument doc;
            switch (_kind)
            {
                case ConnectionGridKind.TcpCcd:
                    doc = _settingsService.LoadTcpConnectionsDynamic("CCD");
                    break;
                case ConnectionGridKind.TcpRobot:
                    doc = _settingsService.LoadTcpConnectionsDynamic("Robot");
                    break;
                case ConnectionGridKind.TcpScanner:
                    doc = _settingsService.LoadTcpConnectionsDynamic("Scanner");
                    break;
                case ConnectionGridKind.TcpSfcPdca:
                    doc = _settingsService.LoadTcpConnectionsDynamic("SFC_PDCA");
                    break;
                default:
                    doc = _settingsService.LoadSerialPortsDynamic();
                    break;
            }

            if (doc != null && doc.HasMissingColumnSchema && !string.IsNullOrWhiteSpace(doc.MissingSchemaMessage))
            {
                _dialogService.ShowWarning(doc.MissingSchemaMessage, "Column Schema Warning");
            }

            foreach (var col in doc.Columns)
            {
                Columns.Add(col);
            }

            foreach (var row in doc.Rows)
            {
                Rows.Add(new Nova.ViewModels.Pages.Recipe.DynamicGridRow(row, doc.Columns));
            }
        }
    }
}
