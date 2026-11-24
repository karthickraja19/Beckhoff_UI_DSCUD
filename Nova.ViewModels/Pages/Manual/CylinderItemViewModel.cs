using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Hardware;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// One pneumatic cylinder on the Manual "Cylinder" board, backed by an
    /// <see cref="ICylinder"/>. Extend/Retract call the device (V3 manual-mode:
    /// fire valve + confirm reed within the configured timeout); the reed chips,
    /// extend/retract state and interlocks follow real IO via
    /// <see cref="RefreshFromHardware"/>, pumped by the parent on each IO tick.
    /// </summary>
    public partial class CylinderItemViewModel : ObservableObject
    {
        private readonly ICylinder _cylinder;

        /// <summary>1-based row number, assigned by the parent when the board is built.</summary>
        public int SerialNo { get; set; }

        public string Name { get; }

        // ---- I/O points (one output per side, one or more sensors per side) ----
        public string ExtendOutput { get; }
        public string RetractOutput { get; }
        public IReadOnlyList<CylinderSensor> WorkSensors { get; }
        public IReadOnlyList<CylinderSensor> HomeSensors { get; }

        // ---- Live state ---------------------------------------------------
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StateText))]
        private bool _isExtended;

        // ---- Interlocks ---------------------------------------------------
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanExtendNow))]
        private bool _safeToExtend;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanRetractNow))]
        private bool _safeToRetract;

        public string StateText => IsExtended ? "Extended" : "Retracted";

        // Button-enablement: the interlock is the only gate. Current position is
        // deliberately not one — both Work and Home stay live so an operator can
        // re-fire the valve at either end (a cylinder that has drifted, or one
        // whose reed never made, still needs to be commandable).
        public bool CanExtendNow => SafeToExtend && !IsExtended;
        public bool CanRetractNow => SafeToRetract && IsExtended;

        public CylinderItemViewModel(ICylinder cylinder)
        {
            _cylinder = cylinder;
            Name = cylinder.CylinderName;
            ExtendOutput = cylinder.ExtendOutputAddress ?? string.Empty;
            RetractOutput = cylinder.RetractOutputAddress ?? string.Empty;
            WorkSensors = BuildSensors(cylinder.ExtendSenseAddresses);
            HomeSensors = BuildSensors(cylinder.RetractSenseAddresses);

            _safeToExtend = true;
            _safeToRetract = true;
            RefreshFromHardware();
        }

        private static IReadOnlyList<CylinderSensor> BuildSensors(IReadOnlyList<string> addresses)
        {
            addresses = addresses ?? new string[0];
            bool compact = addresses.Count > 1;

            var sensors = new List<CylinderSensor>(addresses.Count);
            foreach (var address in addresses)
                sensors.Add(new CylinderSensor(address, false, compact));
            return sensors;
        }

        /// <summary>Keeps this cylinder's live status up to date until the handle is disposed; null for a device that always has it.</summary>
        public IDisposable FollowStatus() => (_cylinder as ILiveStatus)?.FollowStatus();

        /// <summary>
        /// Pulls the latest device snapshot and updates scalar props + reed chips.
        /// Safe to call from the IO poll thread (only INPC scalar setters here).
        /// </summary>
        public void RefreshFromHardware()
        {
            var s = _cylinder.ReadStatus();
            IsExtended = s.IsExtended;
            SafeToExtend = s.SafeToExtend;
            SafeToRetract = s.SafeToRetract;
            ApplySensorStates(WorkSensors, s.ExtendSenseStates);
            ApplySensorStates(HomeSensors, s.RetractSenseStates);
        }

        private static void ApplySensorStates(IReadOnlyList<CylinderSensor> sensors, bool[] states)
        {
            if (states == null) return;
            int n = sensors.Count < states.Length ? sensors.Count : states.Length;
            for (int i = 0; i < n; i++)
                sensors[i].IsOn = states[i];
        }

        [RelayCommand]
        private async Task Extend()
        {
            if (!CanExtendNow) return;
            await _cylinder.ExtendAsync();
            RefreshFromHardware();
        }

        [RelayCommand]
        private async Task Retract()
        {
            if (!CanRetractNow) return;
            await _cylinder.RetractAsync();
            RefreshFromHardware();
        }
    }
}
