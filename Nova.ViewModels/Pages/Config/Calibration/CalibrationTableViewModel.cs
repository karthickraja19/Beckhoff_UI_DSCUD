using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;
using Nova.ViewModels.Pages.Recipe;

namespace Nova.ViewModels.Pages.Calibration
{
    /// <summary>A station in the station box.</summary>
    public sealed class CalibrationStation
    {
        public CalibrationStation(int number)
        {
            Number = number;
        }

        public int Number { get; }

        public string Name => "Station " + Number;
    }

    /// <summary>
    /// One tab of the Config page's Calibration section: the taught positions of one
    /// calibration as a table, and the PLC's Start / Abort / Reset for it.
    /// </summary>
    /// <remarks>
    /// Edit unlocks the table; Save writes its file (Data\Calibration) and then the PLC's
    /// teach table, which the PLC stores. Start runs the calibration on the station in the
    /// station box - the PLC owns the sequence and decides whether it may run; this tab
    /// shows what its calibration manager reports.
    /// </remarks>
    public partial class CalibrationTableViewModel : ObservableObject, IEditableTab, IActivatableTab
    {
        private readonly CalibrationTable _table;
        private readonly ICalibrationService _service;
        private readonly IDialogService _dialog;
        private readonly SynchronizationContext _ui = SynchronizationContext.Current;

        private IDisposable _following;
        private bool _loadingRows;
        private bool _revertingStation;

        private readonly string _title;

        /// <param name="title">What the dialogs call this calibration; the table's own title when null (the 5 axis tab shows two tables under one name).</param>
        public CalibrationTableViewModel(CalibrationTable table, ICalibrationService service, IDialogService dialog, string title = null)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
            _title = title;
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));

            foreach (DynamicColumnSchema column in _table.Columns)
                Columns.Add(column);

            _service.StatusChanged += OnStatusChanged;

            LoadData();
            LoadSetup();
            ShowStatus();
        }

        public string Title => _title ?? _table.Title;

        /// <summary>The calibration this table belongs to.</summary>
        public CalibrationKind Kind => _table.Kind;

        /// <summary>What the table is called in its own heading.</summary>
        public string TableTitle => _table.Title;

        public string Icon => _table.Icon;

        /// <summary>True when the station box picks the table on screen; false when every station is a row.</summary>
        public bool OneTablePerStation => _table.OneTablePerStation;


        // ==================================================================
        // The table
        // ==================================================================

        public ObservableCollection<DynamicColumnSchema> Columns { get; } = new ObservableCollection<DynamicColumnSchema>();

        public ObservableCollection<DynamicGridRow> Rows { get; } = new ObservableCollection<DynamicGridRow>();

        public ObservableCollection<CalibrationStation> Stations { get; } = new ObservableCollection<CalibrationStation>();

        /// <summary>The station Start runs on, the one "Taught" is about and, with a table per station, the one on screen.</summary>
        [ObservableProperty]
        private CalibrationStation _selectedStation;

        [ObservableProperty]
        private DynamicGridRow _selectedRow;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit), nameof(IsReadOnlyMode), nameof(CanStart))]
        private bool _isEditing;

        /// <summary>Why the file could not be read; empty when it was.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit), nameof(HasLoadError))]
        private string _loadError = string.Empty;

        public bool HasLoadError => LoadError.Length > 0;

        public bool CanEdit => !IsEditing && !HasLoadError;

        public bool IsReadOnlyMode => !IsEditing;

        /// <summary>The PLC's bTaught of the selected station: its positions are real and may be used.</summary>
        public bool SelectedTaught
        {
            get { return SelectedStation != null && _table.GetTaught(SelectedStation.Number); }
            set
            {
                if (SelectedStation == null || !IsEditing)
                    return;

                _table.SetTaught(SelectedStation.Number, value);
                OnPropertyChanged();
            }
        }

        partial void OnSelectedStationChanged(CalibrationStation oldValue, CalibrationStation newValue)
        {
            if (_loadingRows || _revertingStation)
                return;

            if (_table.OneTablePerStation)
            {
                // What was typed for the station being left stays with it until Save or Cancel.
                if (IsEditing && oldValue != null && !TakeRows(oldValue.Number))
                {
                    _revertingStation = true;
                    try
                    {
                        SelectedStation = oldValue;
                    }
                    finally
                    {
                        _revertingStation = false;
                    }

                    return;
                }

                ShowRows();
            }
            else if (newValue != null)
            {
                int index = Stations.IndexOf(newValue);
                if (index >= 0 && index < Rows.Count && !ReferenceEquals(SelectedRow, Rows[index]))
                    SelectedRow = Rows[index];
            }

            OnPropertyChanged(nameof(SelectedTaught));
        }

        /// <summary>With every station a row, picking a row picks its station.</summary>
        partial void OnSelectedRowChanged(DynamicGridRow value)
        {
            if (_loadingRows || _table.OneTablePerStation || value == null)
                return;

            int index = Rows.IndexOf(value);
            if (index >= 0 && index < Stations.Count && !ReferenceEquals(SelectedStation, Stations[index]))
                SelectedStation = Stations[index];
        }

        /// <summary>Reads the file again and shows it; the station stays where it was.</summary>
        public void LoadData()
        {
            int station = SelectedStation != null ? SelectedStation.Number : 0;

            _loadingRows = true;
            try
            {
                try
                {
                    _table.Load(_service);
                    LoadError = string.Empty;
                }
                catch (Exception ex)
                {
                    LoadError = ex.Message;
                }

                Stations.Clear();
                if (!HasLoadError)
                {
                    foreach (int number in _table.Stations)
                        Stations.Add(new CalibrationStation(number));
                }

                SelectedStation = Stations.FirstOrDefault(s => s.Number == station) ?? Stations.FirstOrDefault();
            }
            finally
            {
                _loadingRows = false;
            }

            ShowRows();
            OnPropertyChanged(nameof(SelectedTaught));
            OnPropertyChanged(nameof(CanStart));
        }

        private void ShowRows()
        {
            _loadingRows = true;
            try
            {
                Rows.Clear();
                if (SelectedStation != null)
                {
                    foreach (Dictionary<string, string> row in _table.Rows(SelectedStation.Number))
                        Rows.Add(new DynamicGridRow(row, _table.Columns));
                }

                SelectedRow = null;
                if (!_table.OneTablePerStation && SelectedStation != null)
                {
                    int index = Stations.IndexOf(SelectedStation);
                    if (index >= 0 && index < Rows.Count)
                        SelectedRow = Rows[index];
                }
            }
            finally
            {
                _loadingRows = false;
            }
        }

        /// <summary>Takes the rows on screen back into the table. False, with the reason shown, when a cell is not a number.</summary>
        private bool TakeRows(int station)
        {
            if (Rows.Any(r => r.HasErrors))
            {
                _dialog.ShowError("Please correct invalid values first.", Title);
                return false;
            }

            try
            {
                _table.Apply(station, Rows.Select(r => r.ToDictionary()).ToList());
                return true;
            }
            catch (FormatException ex)
            {
                _dialog.ShowError(ex.Message, Title);
                return false;
            }
        }

        [RelayCommand]
        private void Edit()
        {
            if (CanEdit)
                IsEditing = true;
        }

        [RelayCommand]
        private async Task Save()
        {
            if (!IsEditing || SelectedStation == null)
                return;

            if (!TakeRows(SelectedStation.Number))
                return;

            string question = _table.WritesToPlc
                ? "Save the " + Title + " positions and write them to the PLC?"
                : "Save the " + Title + " positions?";
            if (!_dialog.Confirm(question, Title))
                return;

            try
            {
                _table.Save(_service);
            }
            catch (Exception ex)
            {
                // Still editing, so nothing typed is lost.
                _dialog.ShowError("The positions could not be saved: " + ex.Message, Title);
                return;
            }

            IsEditing = false;
            LoadData();

            // The HMI owns these tables (5 axis): the file is all there is to save.
            if (!_table.WritesToPlc)
            {
                _dialog.ShowInfo("Saved.", Title);
                return;
            }

            if (!_service.CanWrite(_table.Kind))
            {
                _dialog.ShowWarning("Saved. Not written to the PLC: it has no teach table for this calibration (Calibration.Teach in PlcSymbols.json).", Title);
                return;
            }

            try
            {
                await _service.WriteToPlcAsync(_table.Kind);
                _dialog.ShowInfo("Saved and written to the PLC.", Title);
            }
            catch (Exception ex)
            {
                _dialog.ShowError("Saved, but not written to the PLC: " + ex.Message, Title);
            }
        }

        [RelayCommand]
        private void Cancel()
        {
            CancelEdit();
        }

        /// <summary>
        /// For a tab that saves several tables together: takes the rows on screen into the table
        /// and says so; false, with the reason shown, when a cell is not a number.
        /// </summary>
        public bool CommitEdit()
        {
            return SelectedStation == null || TakeRows(SelectedStation.Number);
        }

        /// <summary>Writes the table's file (after <see cref="CommitEdit"/>). Throws when it cannot be written.</summary>
        public void WriteFile()
        {
            _table.Save(_service);
        }

        /// <summary>Locks the table again and shows the file as it was saved.</summary>
        public void FinishEdit()
        {
            IsEditing = false;
            LoadData();
        }

        /// <summary>Unlocks the table, as Edit does.</summary>
        public void BeginEdit()
        {
            Edit();
        }

        /// <summary>Leaving the tab abandons the edit: relock and reload from the file.</summary>
        public void CancelEdit()
        {
            if (!IsEditing)
                return;

            IsEditing = false;
            LoadData();
        }


        // ==================================================================
        // 5 axis setup: the plate and the camera image (FiveAxisSettings.json)
        // ==================================================================

        /// <summary>The setup fields are locked while the calibration runs.</summary>
        public bool CanEditSetup => !IsBusy;

        /// <summary>True on the 5-Axis tabs: they show the plate spacing and image size to edit.</summary>
        public bool HasSetup => _table.Kind == CalibrationKind.FiveAxis;

        [ObservableProperty]
        private string _plateSpacingText = string.Empty;

        [ObservableProperty]
        private string _imageWidthText = string.Empty;

        [ObservableProperty]
        private string _imageHeightText = string.Empty;

        private void LoadSetup()
        {
            if (!HasSetup)
                return;

            try
            {
                FiveAxisSetup setup = _service.LoadFiveAxisSetup();
                PlateSpacingText = setup.PlateSpacingMm > 0 ? setup.PlateSpacingMm.ToString(CultureInfo.CurrentCulture) : string.Empty;
                ImageWidthText = setup.ImageWidth > 0 ? setup.ImageWidth.ToString(CultureInfo.CurrentCulture) : string.Empty;
                ImageHeightText = setup.ImageHeight > 0 ? setup.ImageHeight.ToString(CultureInfo.CurrentCulture) : string.Empty;
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
            }
        }

        [RelayCommand]
        private void SaveSetup()
        {
            double spacing;
            int width, height;
            if (!TryNumber(PlateSpacingText, out spacing) || spacing <= 0)
            {
                _dialog.ShowError("The plate spacing must be a number above 0 (mm).", Title);
                return;
            }
            if (!int.TryParse(ImageWidthText.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out width) || width <= 0
                || !int.TryParse(ImageHeightText.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out height) || height <= 0)
            {
                _dialog.ShowError("The image width and height must be whole numbers above 0 (pixels).", Title);
                return;
            }

            try
            {
                _service.SaveFiveAxisSetup(new FiveAxisSetup { PlateSpacingMm = spacing, ImageWidth = width, ImageHeight = height });
            }
            catch (Exception ex)
            {
                _dialog.ShowError("The setup could not be saved: " + ex.Message, Title);
                return;
            }

            _dialog.ShowInfo("Setup saved.", Title);
        }

        private static bool TryNumber(string text, out double value)
        {
            text = (text ?? string.Empty).Trim();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // ==================================================================
        // Running the calibration (the PLC's calibration manager)
        // ==================================================================

        [ObservableProperty]
        private string _statusText = string.Empty;

        /// <summary>"Offline", "Idle", "Running", "Done" or "Error" - the view colours the status with it.</summary>
        [ObservableProperty]
        private string _statusKind = "Offline";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanStart))]
        private bool _isLive;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanStart), nameof(CanEditSetup))]
        private bool _isBusy;

        /// <summary>The PLC is there, nothing runs, and the table is saved.</summary>
        public bool CanStart => _service.CanRun(_table.Kind) && IsLive && !IsBusy && !IsEditing && SelectedStation != null;

        /// <summary>The PLC's calibrations are aborted; the 5 axis one, which the HMI runs, is stopped.</summary>
        public string StopText => _table.Kind == CalibrationKind.FiveAxis ? "Stop" : "Abort";

        [RelayCommand]
        private async Task Start()
        {
            if (!CanStart)
                return;

            int station = SelectedStation.Number;
            if (!_dialog.Confirm("Start the " + Title + " on Station " + station + "? The machine will move.", Title))
                return;

            await Send(() => _service.StartAsync(_table.Kind, station));
        }

        [RelayCommand]
        private Task Abort()
        {
            return Send(() => _service.AbortAsync(_table.Kind));
        }

        [RelayCommand]
        private Task Reset()
        {
            return Send(() => _service.ResetAsync(_table.Kind));
        }

        private async Task Send(Func<Task> command)
        {
            try
            {
                await command();
            }
            catch (Exception ex)
            {
                _dialog.ShowError(ex.Message, Title);
            }
        }

        /// <summary>Follows the manager's status while the tab is on screen.</summary>
        public void Activate()
        {
            if (_following == null)
                _following = _service.FollowStatus();

            // The other 5-Axis tab may have saved the setup since this one was last shown.
            LoadSetup();
            ShowStatus();
        }

        public void Deactivate()
        {
            IDisposable following = _following;
            _following = null;
            if (following != null)
                following.Dispose();
        }

        /// <summary>Raised on any thread.</summary>
        private void OnStatusChanged(object sender, EventArgs e)
        {
            if (_ui == null)
                ShowStatus();
            else
                _ui.Post(_ => ShowStatus(), null);
        }

        private void ShowStatus()
        {
            if (!_service.CanRun(_table.Kind))
            {
                IsLive = false;
                IsBusy = false;
                StatusKind = "Offline";
                StatusText = _service.CannotRunReason(_table.Kind);
                return;
            }

            CalibrationStatus status = _service.Status ?? CalibrationStatus.NotLive;
            IsLive = status.IsLive;

            // A calibration that is under way can still be stopped when the PLC link drops.
            IsBusy = status.IsBusy;

            if (!status.IsLive && !status.IsBusy)
            {
                StatusKind = "Offline";
                StatusText = string.IsNullOrEmpty(status.NotLiveReason) ? "Waiting for the PLC." : status.NotLiveReason;
                return;
            }

            StatusKind = status.IsBusy ? "Running" : status.IsError ? "Error" : status.IsDone ? "Done" : "Idle";
            string what = status.Calibration >= 1
                ? NameOf(status.Calibration) + ", Station " + status.Station + (status.IsBusy ? ", step " + status.Step : string.Empty)
                : string.Empty;
            string message = (status.Message ?? string.Empty).Trim();

            if (what.Length == 0 && message.Length == 0)
                StatusText = "No calibration is running.";
            else if (message.Length == 0)
                StatusText = what;
            else if (what.Length == 0)
                StatusText = message;
            else
                StatusText = what + ": " + message;
        }

        private static string NameOf(int calibration)
        {
            switch ((CalibrationKind)calibration)
            {
                case CalibrationKind.NinePoint: return "9-Point";
                case CalibrationKind.FiveAxis: return "5-Axis";
                case CalibrationKind.NeedleModule: return "Needle Module";
                case CalibrationKind.CameraNeedle: return "Camera Needle";
                default: return "Calibration " + calibration;
            }
        }
    }
}
