using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Machine.Enums;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Settings
{
    public static class EnumExtensions
    {
        public static string GetDescription(this Enum value)
        {
            FieldInfo field = value.GetType().GetField(value.ToString());
            if (field == null) return value.ToString();
            DescriptionAttribute attribute = Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) as DescriptionAttribute;
            return attribute == null ? value.ToString() : attribute.Description;
        }
    }

    /// <summary>A single-select machine mode chip (Production Run / Dry Run / CPK / ...).</summary>
    public partial class DeviceModeItem : ObservableObject
    {
        public string Name { get; }
        public OperationMode Mode { get; }

        [ObservableProperty]
        private bool _isSelected;

        /// <summary>False when the PLC has no run mode behind this one; it cannot be picked.</summary>
        [ObservableProperty]
        private bool _isSupported = true;

        /// <summary>Why it cannot be picked, as the chip's tooltip; null when it can.</summary>
        [ObservableProperty]
        private string _unavailableReason;

        public DeviceModeItem(string name, OperationMode mode, bool isSelected = false)
        {
            Name = name;
            Mode = mode;
            _isSelected = isSelected;
        }
    }

    /// <summary>A bypass toggle row.</summary>
    public partial class DeviceBypassItem : ObservableObject
    {
        public string Name { get; }
        public DeviceBypassKind Kind { get; }

        [ObservableProperty]
        private bool _isOn;

        [ObservableProperty]
        private bool _isEnabled;

        /// <summary>False when the PLC has no bypass behind this switch; it stays off and locked.</summary>
        [ObservableProperty]
        private bool _isSupported = true;

        public DeviceBypassItem(string name, DeviceBypassKind kind, bool isOn = false)
        {
            Name = name;
            Kind = kind;
            _isOn = isOn;
            _isEnabled = false;
        }
    }

    /// <summary>A station that Maintenance Mode can take out of production.</summary>
    public partial class MaintenanceStationItem : ObservableObject
    {
        public int Number { get; }
        public string Name { get; }

        [ObservableProperty]
        private bool _isSelected;

        /// <summary>False when the PLC cannot hold this station out of production.</summary>
        [ObservableProperty]
        private bool _isSupported = true;

        public MaintenanceStationItem(int number, string name)
        {
            Number = number;
            Name = name;
        }
    }

    /// <summary>
    /// "Device Settings" tab: machine mode selector (System Settings), bypass toggles and
    /// the maintenance stations. The TCP/IP + Serial Comm connection grids live in Axis
    /// Parameter Settings.
    /// </summary>
    /// <remarks>
    /// The PLC owns all three. Save asks the PLC through <see cref="IMachineModeLink"/> and
    /// the tab then shows what the PLC reports back: a mode the PLC did not take, or a
    /// bypass it has no variable for, is said so and never shown as set. While the tab is
    /// not being edited it follows the PLC.
    /// </remarks>
    public partial class DeviceSettingsViewModel : ObservableObject
    {
        private readonly IMachineContext _machineContext;
        private readonly Nova.Abstractions.Machine.Interfaces.IMachineOrchestrator _orchestrator;
        private readonly IDialogService _dialogs;
        private readonly IMachineModeLink _link;
        private readonly System.Threading.SynchronizationContext _ui;
        private readonly DeviceBypassItem _sfcBypass;

        public string Title => "Device Settings";

        public ObservableCollection<DeviceModeItem> Modes { get; }
        public ObservableCollection<DeviceBypassItem> Bypasses { get; }

        /// <summary>Stations 1 and 2, picked when Maintenance Mode is selected.</summary>
        public ObservableCollection<MaintenanceStationItem> MaintenanceStations { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        private bool _isEditing;

        [ObservableProperty]
        private bool _isMaintenanceSelected;

        /// <summary>True while Save is writing to the PLC and waiting for it.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        private bool _isSaving;

        /// <summary>One line on what the PLC reports: its mode and whether it takes a change now.</summary>
        [ObservableProperty]
        private string _plcStatusText = string.Empty;

        /// <summary>True when the PLC link is live, so <see cref="PlcStatusText"/> is a status and not a fault.</summary>
        [ObservableProperty]
        private bool _isPlcLive;

        public bool CanEdit => !IsEditing && !IsSaving;

        public DeviceSettingsViewModel(
            IMachineContext machineContext,
            Nova.Abstractions.Machine.Interfaces.IMachineOrchestrator orchestrator,
            IDialogService dialogs,
            IMachineModeLink link = null)
        {
            _machineContext = machineContext ?? throw new System.ArgumentNullException(nameof(machineContext));
            _orchestrator = orchestrator ?? throw new System.ArgumentNullException(nameof(orchestrator));
            _dialogs = dialogs ?? throw new System.ArgumentNullException(nameof(dialogs));
            _link = link;
            _ui = System.Threading.SynchronizationContext.Current;

            Modes = new ObservableCollection<DeviceModeItem>();
            foreach (OperationMode mode in Enum.GetValues(typeof(OperationMode)))
            {
                var item = new DeviceModeItem(mode.GetDescription(), mode, _machineContext.MachineMode == mode);
                if (_link != null)
                {
                    item.IsSupported = _link.CanSelect(mode);
                    item.UnavailableReason = item.IsSupported ? null : _link.WhyNotSelectable(mode);
                }

                Modes.Add(item);
            }

            Bypasses = new ObservableCollection<DeviceBypassItem>();
            foreach (DeviceBypassKind kind in Enum.GetValues(typeof(DeviceBypassKind)))
            {
                var item = new DeviceBypassItem(kind.GetDescription(), kind, _machineContext.GetBypass(kind));
                item.IsSupported = _link == null || _link.CanSet(kind);
                Bypasses.Add(item);
            }

            _sfcBypass = Bypasses.FirstOrDefault(b => b.Kind == DeviceBypassKind.SfcPdca);

            MaintenanceStations = new ObservableCollection<MaintenanceStationItem>
            {
                new MaintenanceStationItem(1, "Station 1"),
                new MaintenanceStationItem(2, "Station 2"),
            };
            foreach (MaintenanceStationItem station in MaintenanceStations)
                station.IsSupported = _link == null || _link.CanHoldOut(station.Number);

            if (_link != null)
                _link.Changed += OnPlcChanged;

            SyncFromPlc();
            ApplyProductionBypassInterlock();
            RefreshBypassEnablement();
        }

        partial void OnIsEditingChanged(bool value)
        {
            RefreshBypassEnablement();
        }

        /// <summary>The PLC reported something else. Raised on the ADS or a worker thread.</summary>
        private void OnPlcChanged(object sender, EventArgs e)
        {
            if (_ui != null)
                _ui.Post(_ => FollowPlc(), null);
            else
                FollowPlc();
        }

        /// <summary>While nobody is editing, the tab shows the PLC; an edit in progress is left alone.</summary>
        private void FollowPlc()
        {
            if (IsEditing || IsSaving)
            {
                RefreshPlcStatus(_link.Read());
                return;
            }

            SyncFromPlc();
            ApplyProductionBypassInterlock();
            RefreshBypassEnablement();
        }

        [RelayCommand]
        private void SelectMode(DeviceModeItem item)
        {
            if (item == null || !item.IsSupported) return;
            foreach (var m in Modes)
                m.IsSelected = ReferenceEquals(m, item);

            IsMaintenanceSelected = item.Mode == OperationMode.Maintenance;
            ApplyProductionBypassInterlock();
            RefreshBypassEnablement();
        }

        [RelayCommand]
        private void Edit()
        {
            SyncFromPlc();
            IsEditing = true;
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task Save()
        {
            if (IsSaving)
                return;

            if (_link == null)
            {
                _dialogs.ShowWarning("Device Settings are not wired to the PLC in this build, so nothing was changed.", Title);
                return;
            }

            var selectedMode = Modes.FirstOrDefault(m => m.IsSelected);
            if (selectedMode == null)
            {
                _dialogs.ShowWarning("Select a machine mode first.", Title);
                return;
            }

            if (!selectedMode.IsSupported)
            {
                _dialogs.ShowWarning(selectedMode.UnavailableReason ?? "The PLC has no run mode for this.", selectedMode.Name);
                return;
            }

            if (selectedMode.Mode == OperationMode.Maintenance && !MaintenanceStations.Any(s => s.IsSelected && s.IsSupported))
            {
                _dialogs.ShowWarning("Select Station 1, Station 2 or both for Maintenance Mode.", "Maintenance Mode");
                return;
            }

            MachineModeState before = _link.Read();
            if (!before.IsLive)
            {
                _dialogs.ShowWarning("Nothing was changed: " + before.NotLiveReason + ".", Title);
                return;
            }

            bool modeChanges = selectedMode.Mode != OperationMode.Maintenance && before.Mode != selectedMode.Mode;
            if (modeChanges)
            {
                // The PLC takes a new run mode only while it stands still and is empty; it
                // says so itself, and nothing is stopped behind the operator's back.
                if (before.ChangeAllowed == false || _orchestrator.IsAutoRunning)
                {
                    _dialogs.ShowWarning(
                        "The machine mode can only be changed while the machine is idle or in manual, and empty."
                        + Environment.NewLine + "Stop the machine, clear the units, then save again.",
                        "Mode Change");
                    return;
                }

                if (!_dialogs.Confirm("Change the machine mode to " + selectedMode.Name + "?", "Confirm Mode Change"))
                {
                    // FAM 2 SET_04: "No" saves nothing - mode and bypasses go back to the PLC's.
                    SyncFromPlc();
                    IsEditing = false;
                    RefreshBypassEnablement();
                    return;
                }
            }
            else if (!_dialogs.Confirm("Do you want to save the Device Settings?", "Confirmation"))
            {
                // FAM 2 SET_04: a bypass-only change was written with no question asked.
                // Now it is asked, and "No" puts every switch back to what the PLC has.
                SyncFromPlc();
                IsEditing = false;
                ApplyProductionBypassInterlock();
                RefreshBypassEnablement();
                return;
            }

            bool isProduction = selectedMode.Mode == OperationMode.Production;
            var request = new MachineModeRequest
            {
                Mode = selectedMode.Mode,
                MaintenanceStations = MaintenanceStations.Where(s => s.IsSelected && s.IsSupported).Select(s => s.Number).ToArray(),
            };
            foreach (var bypass in Bypasses.Where(b => b.IsSupported))
            {
                if (bypass.Kind == DeviceBypassKind.SfcPdca && isProduction)
                    bypass.IsOn = false;

                request.Bypasses[bypass.Kind] = bypass.IsOn;
            }

            MachineModeResult result;
            IsSaving = true;
            try
            {
                result = await _link.ApplyAsync(request);
            }
            catch (Exception ex)
            {
                _dialogs.ShowError("Device Settings could not be sent to the PLC: " + ex.Message, Title);
                return;
            }
            finally
            {
                IsSaving = false;
            }

            IsEditing = false;
            SyncFromPlc();
            ApplyProductionBypassInterlock();
            RefreshBypassEnablement();

            if (result.Success)
            {
                _dialogs.ShowInfo(
                    modeChanges
                        ? "Mode changed to " + selectedMode.Name + ". Press Start to run in this mode."
                        : "Device Settings saved to the PLC.",
                    Title);
            }
            else
            {
                _dialogs.ShowWarning(
                    "The PLC did not take everything:" + Environment.NewLine + string.Join(Environment.NewLine, result.Problems),
                    Title);
            }
        }

        /// <summary>
        /// Shows what the PLC reports - its mode, bypasses and maintenance stations - and
        /// keeps the HMI's own copy in step with it. With the PLC not there, the last known
        /// values stay on screen and the status line says why.
        /// </summary>
        private void SyncFromPlc()
        {
            MachineModeState state = _link != null ? _link.Read() : null;
            RefreshPlcStatus(state);

            if (state != null && state.IsLive)
            {
                if (state.Mode.HasValue)
                    _machineContext.MachineMode = state.Mode.Value;

                foreach (var bypass in state.Bypasses)
                    _machineContext.SetBypass(bypass.Key, bypass.Value);

                _machineContext.MaintenanceStations = state.MaintenanceStations.ToArray();
            }

            // A PLC that reports no mode the HMI knows shows none selected, never a guess.
            bool modeKnown = state == null || !state.IsLive || state.Mode.HasValue;
            foreach (var m in Modes)
                m.IsSelected = modeKnown && m.Mode == _machineContext.MachineMode;

            var held = _machineContext.MaintenanceStations;
            foreach (var station in MaintenanceStations)
                station.IsSelected = held != null && held.Contains(station.Number);

            IsMaintenanceSelected = Modes.Any(m => m.IsSelected && m.Mode == OperationMode.Maintenance);

            foreach (var bypass in Bypasses)
                bypass.IsOn = bypass.IsSupported && _machineContext.GetBypass(bypass.Kind);
        }

        private void RefreshPlcStatus(MachineModeState state)
        {
            if (_link == null)
            {
                IsPlcLive = false;
                PlcStatusText = "Not wired to the PLC.";
                return;
            }

            if (state == null || !state.IsLive)
            {
                IsPlcLive = false;
                PlcStatusText = "PLC: " + (state == null || string.IsNullOrEmpty(state.NotLiveReason) ? "not connected" : state.NotLiveReason);
                return;
            }

            IsPlcLive = true;
            string mode = state.Mode.HasValue
                ? state.Mode.Value.GetDescription()
                : state.PlcModeNumber.HasValue
                    ? (state.PlcModeNumber.Value == 0 ? "no mode selected" : "mode " + state.PlcModeNumber.Value)
                    : "mode not reported";
            string change = state.ChangeAllowed.HasValue
                ? (state.ChangeAllowed.Value ? " - mode change allowed" : " - mode change locked (machine running or not empty)")
                : string.Empty;
            PlcStatusText = "PLC: " + mode + change;
        }

        private void ApplyProductionBypassInterlock()
        {
            bool isProduction = Modes.Any(m => m.IsSelected && m.Mode == OperationMode.Production);
            if (isProduction && _sfcBypass != null)
            {
                _sfcBypass.IsOn = false;
            }
        }

        private void RefreshBypassEnablement()
        {
            bool isProduction = Modes.Any(m => m.IsSelected && m.Mode == OperationMode.Production);
            foreach (var bypass in Bypasses)
            {
                bool blockedByMode = bypass.Kind == DeviceBypassKind.SfcPdca && isProduction;
                bypass.IsEnabled = IsEditing && bypass.IsSupported && !blockedByMode;
            }
        }
    }
}
