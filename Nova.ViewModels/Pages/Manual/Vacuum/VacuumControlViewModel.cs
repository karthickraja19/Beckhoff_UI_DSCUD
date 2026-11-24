using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// ViewModel backing the "Vacuum" tab of the Manual screen. Hydrates its units
    /// from <see cref="IVacuumService"/> (Data\Vacuums.json) and
    /// refreshes feedback each time the IO service reports a change. Only the unit
    /// selected in the dropdown follows live status, and only while the tab is on screen.
    /// </summary>
    public partial class VacuumControlViewModel : ViewModelBase, IActivatableTab
    {
        private readonly IDigitalIOService _io;

        private bool _isActive;

        /// <summary>The selected unit's live status, followed while the tab is on screen.</summary>
        private IDisposable _following;

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

        partial void OnSelectedUnitChanged(VacuumUnitViewModel value)
        {
            FollowSelectedUnit();
        }

        public void Activate()
        {
            if (_isActive)
                return;

            _isActive = true;
            FollowSelectedUnit();
        }

        public void Deactivate()
        {
            if (!_isActive)
                return;

            _isActive = false;
            _following?.Dispose();
            _following = null;
        }

        /// <summary>
        /// Follows the live status of the selected unit, and lets go of the one selected
        /// before. Its values arrive as IO changes once the PLC sends them.
        /// </summary>
        private void FollowSelectedUnit()
        {
            if (!_isActive)
                return;

            IDisposable previous = _following;
            _following = SelectedUnit?.FollowStatus();
            previous?.Dispose();
            SelectedUnit?.RefreshFromHardware();
        }

        // Raised on the ADS thread when a followed signal changes: each unit only sets
        // scalar INPC props (safe); the Units collection itself is never mutated here.
        private void OnIOChanged(object sender, EventArgs e)
        {
            foreach (var unit in Units)
                unit.RefreshFromHardware();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                if (_io != null)
                    _io.IOChanged -= OnIOChanged;
                Deactivate();
            }
            base.Dispose(disposing);
        }
    }
}
