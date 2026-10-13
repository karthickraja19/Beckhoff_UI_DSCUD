using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// ViewModel backing the "Cylinder" tab of the Manual screen — a data-driven
    /// pneumatic state board. Hydrates its <see cref="CylinderItemViewModel"/> cards
    /// from <see cref="ICylinderService"/> (the machine's cylinders.xml catalogue)
    /// and refreshes every card each time the IO service reports a change. The board
    /// shows every cylinder at once, so every cylinder follows live status - while the
    /// tab is on screen, and not otherwise.
    /// </summary>
    public partial class CylinderControlViewModel : ViewModelBase, IActivatableTab
    {
        private readonly IDigitalIOService _io;

        /// <summary>Each cylinder's live status, followed while the tab is on screen.</summary>
        private readonly List<IDisposable> _following = new List<IDisposable>();

        private bool _isActive;

        public ObservableCollection<CylinderItemViewModel> Cylinders { get; }

        [ObservableProperty]
        private int _totalCount;

        [ObservableProperty]
        private int _extendedCount;

        [ObservableProperty]
        private int _retractedCount;

        public CylinderControlViewModel(ICylinderService cylinderService, IDigitalIOService io)
        {
            _io = io;

            Cylinders = new ObservableCollection<CylinderItemViewModel>();
            int serial = 1;
            foreach (var cylinder in cylinderService.GetAllCylinders())
            {
                var item = new CylinderItemViewModel(cylinder) { SerialNo = serial++ };
                item.PropertyChanged += OnCylinderPropertyChanged;
                Cylinders.Add(item);
            }

            RecalculateCounts();

            if (_io != null)
                _io.IOChanged += OnIOChanged;
        }

        public void Activate()
        {
            if (_isActive)
                return;

            _isActive = true;
            foreach (var item in Cylinders)
            {
                IDisposable following = item.FollowStatus();
                if (following != null)
                    _following.Add(following);
            }

            // Off until the PLC sends each reed, rather than what was left from the last visit.
            foreach (var item in Cylinders)
                item.RefreshFromHardware();
        }

        public void Deactivate()
        {
            if (!_isActive)
                return;

            _isActive = false;
            foreach (IDisposable following in _following)
                following.Dispose();
            _following.Clear();
        }

        // Raised on the ADS thread when a followed signal changes: each item only sets
        // scalar INPC props, so this is safe; the Cylinders collection itself is never
        // mutated here.
        private void OnIOChanged(object sender, EventArgs e)
        {
            foreach (var item in Cylinders)
                item.RefreshFromHardware();
        }

        private void OnCylinderPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CylinderItemViewModel.IsExtended))
                RecalculateCounts();
        }

        private void RecalculateCounts()
        {
            TotalCount = Cylinders.Count;
            int extended = 0;
            foreach (var c in Cylinders)
                if (c.IsExtended) extended++;

            ExtendedCount = extended;
            RetractedCount = TotalCount - extended;
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
