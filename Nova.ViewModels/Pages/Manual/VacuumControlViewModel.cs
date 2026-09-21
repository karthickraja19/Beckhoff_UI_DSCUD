using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// ViewModel backing the "Vacuum" tab of the Manual screen. Hydrates its units
    /// from <see cref="IVacuumService"/> (the machine's Vacuums.xml catalogue) and
    /// refreshes feedback on each digital-IO poll tick.
    /// </summary>
    public partial class VacuumControlViewModel : ViewModelBase
    {
        private readonly IDigitalIOService _io;

        public ObservableCollection<VacuumUnitViewModel> Units { get; }

        [ObservableProperty]
        private VacuumUnitViewModel _selectedUnit;

        public VacuumControlViewModel(IVacuumService vacuumService, IDigitalIOService io)
        {
            _io = io;

            Units = new ObservableCollection<VacuumUnitViewModel>();
            foreach (var vacuum in vacuumService.GetAllVacuums())
                Units.Add(new VacuumUnitViewModel(vacuum));

            _selectedUnit = Units.Count > 0 ? Units[0] : null;

            if (_io != null)
                _io.IOChanged += OnIOChanged;
        }

        // IO poll thread: each unit only sets scalar INPC props (safe); the Units
        // collection itself is never mutated here.
        private void OnIOChanged(object sender, EventArgs e)
        {
            foreach (var unit in Units)
                unit.RefreshFromHardware();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed && _io != null)
                _io.IOChanged -= OnIOChanged;
            base.Dispose(disposing);
        }
    }
}
