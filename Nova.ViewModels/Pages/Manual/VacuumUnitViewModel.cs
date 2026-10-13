using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Hardware;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>Suction / off / blow state of a vacuum unit.</summary>
    public enum VacuumMode
    {
        Off,
        Vacuum,
        Blow
    }

    /// <summary>
    /// A single vacuum unit on the Manual "Vacuum" tab, backed by an
    /// <see cref="IVacuum"/>. Vacuum/Off/Blow call the device (V3 manual-mode: fire
    /// output + confirm feedback within the configured timeout); FeedbackActive
    /// follows the real feedback input via <see cref="RefreshFromHardware"/>.
    /// </summary>
    public partial class VacuumUnitViewModel : ObservableObject
    {
        private readonly IVacuum _vacuum;

        public string Name { get; }
        public string FeedbackAddress { get; }

        // Live feedback-input state (true = vacuum sensed / stable).
        [ObservableProperty]
        private bool _feedbackActive;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsVacuum))]
        [NotifyPropertyChangedFor(nameof(IsOff))]
        [NotifyPropertyChangedFor(nameof(IsBlow))]
        private VacuumMode _mode;

        public bool IsVacuum => Mode == VacuumMode.Vacuum;
        public bool IsOff => Mode == VacuumMode.Off;
        public bool IsBlow => Mode == VacuumMode.Blow;

        public VacuumUnitViewModel(IVacuum vacuum)
        {
            _vacuum = vacuum;
            Name = vacuum.VacuumName;
            FeedbackAddress = vacuum.FeedbackAddress;
            _mode = VacuumMode.Off;
            RefreshFromHardware();
        }

        /// <summary>Keeps this unit's live status up to date until the handle is disposed; null for a device that always has it.</summary>
        public IDisposable FollowStatus() => (_vacuum as ILiveStatus)?.FollowStatus();

        /// <summary>Pulls the latest device snapshot (safe from the IO poll thread — scalar props only).</summary>
        public void RefreshFromHardware()
        {
            var s = _vacuum.ReadStatus();
            FeedbackActive = s.FeedbackOn;
            if (s.VacuumCommanded) Mode = VacuumMode.Vacuum;
            else if (s.BlowCommanded) Mode = VacuumMode.Blow;
            else Mode = VacuumMode.Off;
        }

        [RelayCommand]
        private async Task SetVacuum()
        {
            Mode = VacuumMode.Vacuum;
            await _vacuum.VacuumOnAsync();
            RefreshFromHardware();
        }

        [RelayCommand]
        private async Task SetOff()
        {
            Mode = VacuumMode.Off;
            await _vacuum.OffAsync();
            RefreshFromHardware();
        }

        [RelayCommand]
        private async Task SetBlow()
        {
            Mode = VacuumMode.Blow;
            await _vacuum.BlowOnAsync();
            RefreshFromHardware();
        }
    }
}
