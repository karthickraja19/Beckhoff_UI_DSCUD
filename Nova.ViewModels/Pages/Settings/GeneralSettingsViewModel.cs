using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data.Repositories;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>
    /// "General" settings tab — shift configuration (V3 Shift Settings + Update Shift
    /// Downtime/Breaktime). Card 1 edits the active shift window; card 2 records planned
    /// downtime/breaktime for a given date + shift.
    /// </summary>
    public partial class GeneralSettingsViewModel : ObservableObject
    {
        private readonly IDialogService _dialogService;
        private readonly IShiftSettingsRepository _shiftRepository;

        private IReadOnlyList<ShiftDefinition> _shiftDefinitions = new ShiftDefinition[0];

        private string _previousShift;
        private string _previousStartTime;
        private string _previousEndTime;
        private DateTime _previousUpdateDate;
        private string _previousUpdateShift;
        private string _previousPlannedDowntime;
        private string _previousPlannedBreaktime;

        public string Title => "General";

        /// <summary>Every configured shift — the editor combo, as V3's cmbShift is filled.</summary>
        public ObservableCollection<string> Shifts { get; } =
            new ObservableCollection<string> { "A", "B", "C" };

        /// <summary>
        /// The downtime card's shift list. V3's LoadShiftCombo fills this one
        /// (shiftdata) differently from the editor combo: while the current shift runs
        /// within a single day, only shifts that have not finished yet are offered; for
        /// an overnight current shift, all of them are.
        /// </summary>
        public ObservableCollection<string> DowntimeShifts { get; } =
            new ObservableCollection<string> { "A", "B", "C" };

        // ---- Card 1: Shift settings ---------------------------------------
        [ObservableProperty]
        private string _selectedShift = "A";

        [ObservableProperty]
        private string _startTime = "06:00:00";

        [ObservableProperty]
        private string _endTime = "14:00:00";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        private bool _isEditing;

        public bool CanEdit => !IsEditing;

        public GeneralSettingsViewModel(IDialogService dialogService, IShiftSettingsRepository shiftRepository = null)
        {
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _shiftRepository = shiftRepository;

            _previousShift = SelectedShift;
            _previousStartTime = StartTime;
            _previousEndTime = EndTime;
            _previousUpdateDate = UpdateDate;
            _previousUpdateShift = UpdateShift;
            _previousPlannedDowntime = PlannedDowntime;
            _previousPlannedBreaktime = PlannedBreaktime;

            // Built by hand rather than with [RelayCommand(CanExecute = ...)]: that
            // attribute property is init-only and this project pins LangVersion 8.0.
            UpdateCommand = new AsyncRelayCommand(Update, CanUpdate);
            RecalculateCommand = new AsyncRelayCommand(Recalculate, CanRecalculate);

            // Fire-and-forget: the constructor cannot await, and the combos are already
            // populated with sensible defaults until the master arrives.
            if (_shiftRepository != null)
                _ = LoadShiftsAsync();
        }

        /// <summary>
        /// Fills both combos from [ShiftMasterData] and shows the selected shift's real
        /// window, replacing the hardcoded A/B/C defaults. Mirrors LoadShiftCombo().
        /// </summary>
        private async Task LoadShiftsAsync()
        {
            try
            {
                _shiftDefinitions = await _shiftRepository.GetShiftsAsync();
                if (_shiftDefinitions == null || _shiftDefinitions.Count == 0)
                    return;

                Shifts.Clear();
                foreach (var shift in _shiftDefinitions)
                    Shifts.Add(shift.ShiftName);

                // V3's filter for the downtime combo: it depends on the selected date as
                // well as on whether the current shift spans midnight.
                RefreshDowntimeShifts();

                SelectedShift = Shifts.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(UpdateShift))
                    UpdateShift = SelectedShift;
                ApplySelectedShiftTimes();

                // The shift master decides which of Update / Recalculate applies, so
                // re-evaluate both now that it has arrived.
                RefreshShiftDataCommands();

                _previousShift = SelectedShift;
                _previousUpdateShift = UpdateShift;
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Could not read the shift master: " + ex.Message, "Settings");
            }
        }

        partial void OnSelectedShiftChanged(string value) => ApplySelectedShiftTimes();

        private void ApplySelectedShiftTimes()
        {
            var shift = _shiftDefinitions.FirstOrDefault(s =>
                string.Equals(s.ShiftName, SelectedShift, StringComparison.OrdinalIgnoreCase));
            if (shift == null)
                return;

            StartTime = shift.StartTime.ToString(@"hh\:mm\:ss");
            EndTime = shift.EndTime.ToString(@"hh\:mm\:ss");
            _previousStartTime = StartTime;
            _previousEndTime = EndTime;
        }

        [RelayCommand]
        private void Edit() => IsEditing = true;

        [RelayCommand]
        private async Task Save()
        {
            if (string.IsNullOrWhiteSpace(SelectedShift))
            {
                _dialogService.ShowError("Please select a shift before saving.", "Settings");
                return;
            }

            if (!TryParseShiftTime(StartTime, "start time", out var startTime) ||
                !TryParseShiftTime(EndTime, "end time", out var endTime))
            {
                return;
            }

            if (startTime == endTime)
            {
                _dialogService.ShowError("Shift times are mismatching. Start Time and End Time cannot be the same.", "Settings");
                return;
            }

            if (!IsValidShiftConfiguration(startTime, endTime))
            {
                _dialogService.ShowError("Invalid shift times. A shift cannot exceed 14 hours.", "Settings");
                return;
            }

            if (_shiftRepository == null)
            {
                _dialogService.ShowError("Shift settings cannot be saved without a database connection.", "Settings");
                return;
            }

            try
            {
                await _shiftRepository.UpdateShiftAsync(SelectedShift, startTime, endTime);
                // Re-read so the combos and times reflect what was actually stored.
                await LoadShiftsAsync();
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Could not save the shift: " + ex.Message, "Settings");
                return;
            }

            _previousShift = SelectedShift;
            _previousStartTime = StartTime;
            _previousEndTime = EndTime;
            IsEditing = false;
            _dialogService.ShowInfo("Shift Data Saved Successfully!!", "Settings");
        }

        public static bool IsValidShiftConfiguration(TimeSpan startTime, TimeSpan endTime)
        {
            double shiftDurationHours;
            if (endTime > startTime)
            {
                shiftDurationHours = (endTime - startTime).TotalHours;
            }
            else
            {
                shiftDurationHours = (endTime.Add(TimeSpan.FromHours(24)) - startTime).TotalHours;
            }

            return shiftDurationHours > 0 && shiftDurationHours <= 14.0;
        }

        // ---- Card 2: Update shift downtime / breaktime --------------------
        [ObservableProperty]
        private DateTime _updateDate = DateTime.Today;

        [ObservableProperty]
        private string _updateShift = "B";

        [ObservableProperty]
        private string _plannedDowntime = "0";

        [ObservableProperty]
        private string _plannedBreaktime = "0";

        /// <summary>
        /// Update writes the plan for a shift that has not finished; Recalculate
        /// corrects one that has. Exactly one of them applies to the selected date and
        /// shift, so V3 enables one and disables the other
        /// (shiftdata_SelectedIndexChanged / shiftdate_ValueChanged). Both were live
        /// here, which let an operator "update" a finished shift without the recompute
        /// that shift's OEE needs — or recalculate one that has not run yet.
        /// </summary>
        public IAsyncRelayCommand UpdateCommand { get; }

        public IAsyncRelayCommand RecalculateCommand { get; }

        /// <summary>
        /// True once the selected shift's end has passed on the selected date. V3 adds
        /// a day when the shift spans midnight, so an overnight shift is only finished
        /// after its end time on the following day.
        /// </summary>
        private bool SelectedUpdateShiftHasFinished()
        {
            var shift = _shiftDefinitions.FirstOrDefault(s =>
                string.Equals(s.ShiftName, UpdateShift, StringComparison.OrdinalIgnoreCase));

            // Unknown shift (no master loaded yet): V3 falls back to Update.
            if (shift == null)
                return false;

            DateTime shiftEnd = UpdateDate.Date.Add(shift.EndTime);
            if (shift.StartTime > shift.EndTime)
                shiftEnd = shiftEnd.AddDays(1);

            return shiftEnd < DateTime.Now;
        }

        private bool CanUpdate() => !SelectedUpdateShiftHasFinished();

        private bool CanRecalculate() => SelectedUpdateShiftHasFinished();

        /// <summary>Re-evaluates which of the two buttons applies. Called whenever the
        /// date, the shift or the shift master changes.</summary>
        private void RefreshShiftDataCommands()
        {
            // Null-guarded: the observable-property hooks below can run while the
            // constructor is still assigning the commands.
            UpdateCommand?.NotifyCanExecuteChanged();
            RecalculateCommand?.NotifyCanExecuteChanged();
        }

        partial void OnUpdateShiftChanged(string value) => RefreshShiftDataCommands();

        /// <summary>
        /// V3's shiftdate_ValueChanged: today only offers shifts that have not finished
        /// yet (unless the current shift is overnight), while any other date offers all
        /// of them — otherwise a past date could not select the very shifts Recalculate
        /// exists to correct.
        /// </summary>
        partial void OnUpdateDateChanged(DateTime value)
        {
            RefreshDowntimeShifts();
            RefreshShiftDataCommands();
        }

        private void RefreshDowntimeShifts()
        {
            if (_shiftDefinitions == null || _shiftDefinitions.Count == 0)
                return;

            bool currentShiftIsOvernight = _shiftDefinitions.Any(s => s.StartTime > s.EndTime);
            var choices = (UpdateDate.Date == DateTime.Today && !currentShiftIsOvernight)
                ? _shiftDefinitions.Where(s => s.EndTime > DateTime.Now.TimeOfDay).ToList()
                : _shiftDefinitions.ToList();

            string previous = UpdateShift;
            DowntimeShifts.Clear();
            foreach (var shift in choices)
                DowntimeShifts.Add(shift.ShiftName);

            UpdateShift = DowntimeShifts.Contains(previous)
                ? previous
                : DowntimeShifts.FirstOrDefault();
        }

        private async Task Update()
        {
            if (!TryReadPlannedMinutes(PlannedDowntime, "Downtime", out var downtime) ||
                !TryReadPlannedMinutes(PlannedBreaktime, "Breaktime", out var breaktime))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(UpdateShift))
            {
                _dialogService.ShowError("Please select a shift before updating downtime/breaktime.", "Settings");
                return;
            }

            if (_shiftRepository == null)
            {
                _dialogService.ShowError("Downtime/breaktime cannot be saved without a database connection.", "Settings");
                return;
            }

            try
            {
                await _shiftRepository.UpdatePlannedDowntimeAsync(UpdateDate, UpdateShift, downtime, breaktime);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Could not update the shift: " + ex.Message, "Settings");
                return;
            }

            _previousUpdateDate = UpdateDate;
            _previousUpdateShift = UpdateShift;
            _previousPlannedDowntime = downtime.ToString();
            _previousPlannedBreaktime = breaktime.ToString();
            PlannedDowntime = _previousPlannedDowntime;
            PlannedBreaktime = _previousPlannedBreaktime;
            _dialogService.ShowInfo("Shift Data Updated Successfully!!", "Settings");
        }

        [RelayCommand]
        private void Cancel()
        {
            SelectedShift = _previousShift;
            StartTime = _previousStartTime;
            EndTime = _previousEndTime;
            UpdateDate = _previousUpdateDate;
            UpdateShift = _previousUpdateShift;
            PlannedDowntime = _previousPlannedDowntime;
            PlannedBreaktime = _previousPlannedBreaktime;
        }

        /// <summary>
        /// Corrects a shift that has already finished: same write as Update, but the
        /// recompute targets the chosen shift and date instead of the current shift.
        /// V3's recalculate_Click reads the *editor* combo here while update_Click reads
        /// the downtime combo; both use the downtime card's shift here, since that is
        /// the card the date and minutes belong to.
        /// </summary>
        private async Task Recalculate()
        {
            if (!TryReadPlannedMinutes(PlannedDowntime, "Downtime", out var downtime) ||
                !TryReadPlannedMinutes(PlannedBreaktime, "Breaktime", out var breaktime))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(UpdateShift))
            {
                _dialogService.ShowError("Please select a shift before recalculating.", "Settings");
                return;
            }

            if (_shiftRepository == null)
            {
                _dialogService.ShowError("Shift data cannot be recalculated without a database connection.", "Settings");
                return;
            }

            try
            {
                await _shiftRepository.RecalculateShiftAsync(UpdateDate, UpdateShift, downtime, breaktime);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Could not recalculate the shift: " + ex.Message, "Settings");
                return;
            }

            _dialogService.ShowInfo("Shift Data Re-Calculated Successfully!!", "Settings");
        }

        private bool TryReadPlannedMinutes(string text, string label, out int value)
        {
            if (!int.TryParse(text, out value) || value < 0 || value > 480)
            {
                _dialogService.ShowError(label + " must be a number between 0 and 480.", "Settings");
                return false;
            }

            return true;
        }

        private bool TryParseShiftTime(string rawText, string label, out TimeSpan parsed)
        {
            if (!TimeSpan.TryParse(rawText, out parsed))
            {
                _dialogService.ShowError("Please enter a valid " + label + " in HH:mm:ss format.", "Settings");
                return false;
            }

            return true;
        }
    }
}
