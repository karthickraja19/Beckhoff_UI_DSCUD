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

        public DeviceBypassItem(string name, DeviceBypassKind kind, bool isOn = false)
        {
            Name = name;
            Kind = kind;
            _isOn = isOn;
            _isEnabled = false;
        }
    }

    /// <summary>
    /// "Device Settings" tab: machine mode selector (System Settings) and bypass
    /// toggles. The TCP/IP + Serial Comm connection grids live in Axis Parameter Settings.
    /// </summary>
    public partial class DeviceSettingsViewModel : ObservableObject
    {
        private readonly IMachineContext _machineContext;
        private readonly Nova.Abstractions.Machine.Interfaces.IMachineOrchestrator _orchestrator;
        private readonly IDialogService _dialogs;
        private readonly DeviceBypassItem _sfcBypass;

        public string Title => "Device Settings";

        public ObservableCollection<DeviceModeItem> Modes { get; }
        public ObservableCollection<DeviceBypassItem> Bypasses { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        private bool _isEditing;

        public bool CanEdit => !IsEditing;

        public DeviceSettingsViewModel(
            IMachineContext machineContext,
            Nova.Abstractions.Machine.Interfaces.IMachineOrchestrator orchestrator,
            IDialogService dialogs)
        {
            _machineContext = machineContext ?? throw new System.ArgumentNullException(nameof(machineContext));
            _orchestrator = orchestrator ?? throw new System.ArgumentNullException(nameof(orchestrator));
            _dialogs = dialogs ?? throw new System.ArgumentNullException(nameof(dialogs));

            Modes = new ObservableCollection<DeviceModeItem>();
            foreach (OperationMode mode in Enum.GetValues(typeof(OperationMode)))
            {
                Modes.Add(new DeviceModeItem(mode.GetDescription(), mode, _machineContext.MachineMode == mode));
            }
            
            if (!Modes.Any(m => m.IsSelected) && Modes.Count > 0)
            {
                Modes[0].IsSelected = true;
            }

            Bypasses = new ObservableCollection<DeviceBypassItem>();
            foreach (DeviceBypassKind kind in Enum.GetValues(typeof(DeviceBypassKind)))
            {
                Bypasses.Add(new DeviceBypassItem(kind.GetDescription(), kind, _machineContext.GetBypass(kind)));
            }

            _sfcBypass = Bypasses.FirstOrDefault(b => b.Kind == DeviceBypassKind.SfcPdca);
            ApplyProductionBypassInterlock();
            RefreshBypassEnablement();
        }

        partial void OnIsEditingChanged(bool value)
        {
            RefreshBypassEnablement();
        }

        [RelayCommand]
        private void SelectMode(DeviceModeItem item)
        {
            if (item == null) return;
            foreach (var m in Modes)
                m.IsSelected = ReferenceEquals(m, item);

            ApplyProductionBypassInterlock();
            RefreshBypassEnablement();
        }

        [RelayCommand]
        private void Edit()
        {
            SyncFromContext();
            IsEditing = true;
        }

        [RelayCommand]
        private void Save()
        {
            var selectedMode = Modes.FirstOrDefault(m => m.IsSelected);
            if (selectedMode != null && selectedMode.Mode != _machineContext.MachineMode)
            {
                _orchestrator.Stop();

                bool proceed = _dialogs.Confirm(
                    "Sequence stopped." + System.Environment.NewLine +
                    "You need to start again for selected mode: " + selectedMode.Name + "." +
                    System.Environment.NewLine + System.Environment.NewLine +
                    "Do you want to continue?",
                    "Confirm Mode Change");

                if (proceed)
                {
                    _machineContext.MachineMode = selectedMode.Mode;
                    _dialogs.ShowInfo(
                        "Mode changed to " + selectedMode.Name + ". Please start the sequence again.",
                        "Mode Changed");
                }
                else
                {
                    SyncModeSelection();
                }
            }

            bool isProduction = Modes.Any(m => m.IsSelected && m.Mode == OperationMode.Production);
            
            foreach (var bypass in Bypasses)
            {
                if (bypass.Kind == DeviceBypassKind.SfcPdca && isProduction)
                {
                    bypass.IsOn = false;
                }
                _machineContext.SetBypass(bypass.Kind, bypass.IsOn);
            }

            IsEditing = false;
            ApplyProductionBypassInterlock();
            RefreshBypassEnablement();
        }

        private void SyncModeSelection()
        {
            var live = _machineContext.MachineMode;
            foreach (var m in Modes)
                m.IsSelected = m.Mode == live;
        }

        private void SyncFromContext()
        {
            SyncModeSelection();
            foreach (var bypass in Bypasses)
            {
                bypass.IsOn = _machineContext.GetBypass(bypass.Kind);
            }
            ApplyProductionBypassInterlock();
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
                bypass.IsEnabled = IsEditing && !blockedByMode;
            }
        }
    }
}
