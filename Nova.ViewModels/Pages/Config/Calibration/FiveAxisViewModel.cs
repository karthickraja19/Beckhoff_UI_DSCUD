using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Calibration
{
    /// <summary>
    /// The 5-Axis tab: the translation table (point1..9) and the rotation table (point10..34)
    /// of one station on one tab, with one Edit / Save, one station box and one Start / Stop /
    /// Reset - the calibration is one sequence that uses both tables.
    /// </summary>
    /// <remarks>
    /// Each table is a <see cref="CalibrationTableViewModel"/> (its grid, its file). The run
    /// buttons, the status and the setup belong to <see cref="Run"/>, the translation one;
    /// the station box drives both so they always show the same station.
    /// </remarks>
    public sealed partial class FiveAxisViewModel : ObservableObject, IEditableTab, IActivatableTab
    {
        private const string Name = "5-Axis Calibration";

        private readonly IDialogService _dialog;
        private bool _syncingStation;

        public FiveAxisViewModel(ICalibrationService service, IDialogService dialog)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));

            Translation = new CalibrationTableViewModel(new FiveAxisTranslationTable(), service, dialog, Name);
            Rotation = new CalibrationTableViewModel(new FiveAxisRotationTable(), service, dialog, Name);

            Translation.PropertyChanged += OnTranslationChanged;
            Rotation.PropertyChanged += OnRotationChanged;
        }

        public string Title => Name;

        public string Icon => "Move";

        /// <summary>point1 .. point9. Also the one that holds the Start / Stop / Reset, the status and the setup.</summary>
        public CalibrationTableViewModel Translation { get; }

        /// <summary>point10 .. point34.</summary>
        public CalibrationTableViewModel Rotation { get; }

        /// <summary>The run buttons and status: the same object as <see cref="Translation"/>.</summary>
        public CalibrationTableViewModel Run => Translation;


        // ==================================================================
        // Station: one box for both tables
        // ==================================================================

        private void OnTranslationChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(CalibrationTableViewModel.SelectedStation):
                    FollowStation(Translation, Rotation);
                    OnPropertyChanged(nameof(SelectedTaught));
                    break;
                case nameof(CalibrationTableViewModel.IsEditing):
                case nameof(CalibrationTableViewModel.CanEdit):
                    OnPropertyChanged(nameof(IsEditing));
                    OnPropertyChanged(nameof(CanEdit));
                    break;
                case nameof(CalibrationTableViewModel.SelectedTaught):
                    OnPropertyChanged(nameof(SelectedTaught));
                    break;
            }
        }

        private void OnRotationChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(CalibrationTableViewModel.SelectedStation):
                    FollowStation(Rotation, Translation);
                    OnPropertyChanged(nameof(SelectedTaught));
                    break;
                case nameof(CalibrationTableViewModel.IsEditing):
                case nameof(CalibrationTableViewModel.CanEdit):
                    OnPropertyChanged(nameof(IsEditing));
                    OnPropertyChanged(nameof(CanEdit));
                    break;
                case nameof(CalibrationTableViewModel.SelectedTaught):
                    OnPropertyChanged(nameof(SelectedTaught));
                    break;
            }
        }

        /// <summary>The table that moved to another station takes the other one along.</summary>
        private void FollowStation(CalibrationTableViewModel moved, CalibrationTableViewModel other)
        {
            if (_syncingStation)
                return;

            int number = moved.SelectedStation != null ? moved.SelectedStation.Number : 0;
            int otherNumber = other.SelectedStation != null ? other.SelectedStation.Number : 0;
            if (number == otherNumber)
                return;

            _syncingStation = true;
            try
            {
                foreach (CalibrationStation station in other.Stations)
                {
                    if (station.Number == number)
                    {
                        other.SelectedStation = station;
                        break;
                    }
                }

                // The other table refused (a cell typed in it is not a number): both stay where they were.
                otherNumber = other.SelectedStation != null ? other.SelectedStation.Number : 0;
                if (otherNumber != number)
                {
                    foreach (CalibrationStation station in moved.Stations)
                    {
                        if (station.Number == otherNumber)
                        {
                            moved.SelectedStation = station;
                            break;
                        }
                    }
                }
            }
            finally
            {
                _syncingStation = false;
            }
        }

        /// <summary>The station both tables are showing, and the one Start runs on.</summary>
        public CalibrationStation SelectedStation
        {
            get { return Translation.SelectedStation; }
            set { Translation.SelectedStation = value; }
        }

        /// <summary>The PLC-side "taught" mark of the station, for both tables together.</summary>
        public bool SelectedTaught
        {
            get { return Translation.SelectedTaught && Rotation.SelectedTaught; }
            set
            {
                Translation.SelectedTaught = value;
                Rotation.SelectedTaught = value;
                OnPropertyChanged();
            }
        }


        // ==================================================================
        // Edit / Cancel / Save for both tables
        // ==================================================================

        /// <summary>Either table is unlocked.</summary>
        public bool IsEditing => Translation.IsEditing || Rotation.IsEditing;

        /// <summary>Both files could be read and nothing is being edited.</summary>
        public bool CanEdit => Translation.CanEdit && Rotation.CanEdit;

        [RelayCommand]
        private void Edit()
        {
            if (!CanEdit)
                return;

            Translation.BeginEdit();
            Rotation.BeginEdit();
        }

        [RelayCommand]
        private void Cancel()
        {
            CancelEdit();
        }

        public void CancelEdit()
        {
            Translation.CancelEdit();
            Rotation.CancelEdit();
        }

        [RelayCommand]
        private void Save()
        {
            if (!IsEditing)
                return;

            // Both tables are checked before either is written.
            if (!Translation.CommitEdit() || !Rotation.CommitEdit())
                return;

            if (!_dialog.Confirm("Save the " + Name + " positions (translation and rotation)?", Name))
                return;

            try
            {
                Translation.WriteFile();
                Rotation.WriteFile();
            }
            catch (Exception ex)
            {
                // Still editing, so nothing typed is lost.
                _dialog.ShowError("The positions could not be saved: " + ex.Message, Name);
                return;
            }

            Translation.FinishEdit();
            Rotation.FinishEdit();
            _dialog.ShowInfo("Saved.", Name);
        }


        // ==================================================================
        // Activation
        // ==================================================================

        public void Activate()
        {
            Translation.Activate();
            Rotation.Activate();
        }

        public void Deactivate()
        {
            Translation.Deactivate();
            Rotation.Deactivate();
        }
    }
}
