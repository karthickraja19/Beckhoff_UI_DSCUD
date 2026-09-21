using System;
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
    /// and refreshes every card on each digital-IO poll tick.
    /// </summary>
    public partial class CylinderControlViewModel : ViewModelBase
    {
        private readonly IDigitalIOService _io;

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

        // Raised on the IO poll thread: each item only sets scalar INPC props, so
        // this is safe; the Cylinders collection itself is never mutated here.
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
            if (disposing && !IsDisposed && _io != null)
                _io.IOChanged -= OnIOChanged;
            base.Dispose(disposing);
        }
    }
}
