using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>A status lamp: on, off, or unknown (no value from the PLC).</summary>
    public partial class DeviceIndicator : ObservableObject
    {
        public DeviceIndicator(string label, string key, bool onIsBad)
        {
            Label = label;
            Key = key;
            OnIsBad = onIsBad;
        }

        public string Label { get; }

        public string Key { get; }

        /// <summary>True for a fault lamp (red when on), false for a good-state lamp (green when on).</summary>
        public bool OnIsBad { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsOn), nameof(IsKnown), nameof(IsGood), nameof(IsBad))]
        private bool? _state;

        public bool IsKnown => State.HasValue;

        public bool IsOn => State == true;

        public bool IsGood => State == true && !OnIsBad;

        public bool IsBad => State == true && OnIsBad;
    }

    /// <summary>A value the device reports, shown read only.</summary>
    public partial class DeviceReading : ObservableObject
    {
        public DeviceReading(string label, string key, string unit, int decimals, bool isText)
        {
            Label = label;
            Key = key;
            Unit = unit ?? string.Empty;
            Decimals = decimals;
            IsText = isText;
        }

        public string Label { get; }

        public string Key { get; }

        public string Unit { get; }

        public int Decimals { get; }

        public bool IsText { get; }

        /// <summary>"-" until the PLC reports it.</summary>
        [ObservableProperty]
        private string _value = "-";
    }

    public sealed class DeviceReadingGroup
    {
        public DeviceReadingGroup(string title, IEnumerable<DeviceReading> items)
        {
            Title = title;
            Items = items.ToList();
        }

        public string Title { get; }

        public IReadOnlyList<DeviceReading> Items { get; }
    }

    /// <summary>A value the operator enters and the PLC receives.</summary>
    public partial class DeviceSetting : ObservableObject
    {
        public DeviceSetting(string label, string key, string unit, double min, double max, int decimals,
            string readBackKey = null, string visibleWhenKey = null, bool visibleWhenValue = true)
        {
            Label = label;
            Key = key;
            Unit = unit ?? string.Empty;
            Min = min;
            Max = max;
            Decimals = decimals;
            ReadBackKey = string.IsNullOrEmpty(readBackKey) ? key : readBackKey;
            VisibleWhenKey = visibleWhenKey;
            VisibleWhenValue = visibleWhenValue;
        }

        public string Label { get; }

        public string Key { get; }

        public string Unit { get; }

        public double Min { get; }

        public double Max { get; }

        public int Decimals { get; }

        /// <summary>The key the value in use is read from (often the same one).</summary>
        public string ReadBackKey { get; }

        /// <summary>A flag that decides whether this setting applies (e.g. dual fluid), or null when it always does.</summary>
        public string VisibleWhenKey { get; }

        public bool VisibleWhenValue { get; }

        public string Range => Min.ToString("0.###", CultureInfo.InvariantCulture) + " to " + Max.ToString("0.###", CultureInfo.InvariantCulture)
                               + (Unit.Length > 0 ? " " + Unit : string.Empty);

        /// <summary>What the operator typed.</summary>
        [ObservableProperty]
        private string _text = string.Empty;

        /// <summary>The value in use, as the PLC reports it.</summary>
        [ObservableProperty]
        private string _current = "-";

        [ObservableProperty]
        private bool _isVisible = true;

        [ObservableProperty]
        private string _error;

        public bool TryParse(out double value)
        {
            return double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }
    }

    /// <summary>Settings written together, optionally followed by a command that sends them on to the device.</summary>
    public sealed class DeviceSettingGroup
    {
        public DeviceSettingGroup(string title, string applyLabel, string applyKey, IEnumerable<DeviceSetting> items)
        {
            Title = title;
            ApplyLabel = applyLabel;
            ApplyKey = applyKey;
            Items = items.ToList();
        }

        public string Title { get; }

        public string ApplyLabel { get; }

        /// <summary>A command pressed after the values are written (the dispenser's Save), or null.</summary>
        public string ApplyKey { get; }

        public IReadOnlyList<DeviceSetting> Items { get; }

        public IRelayCommand ApplyCommand { get; internal set; }
    }

    /// <summary>A button: a one-shot command the PLC acts on.</summary>
    public sealed class DeviceCommand
    {
        public DeviceCommand(string label, string key, string icon, bool isPrimary = false)
        {
            Label = label;
            Key = key;
            Icon = icon;
            IsPrimary = isPrimary;
        }

        public string Label { get; }

        public string Key { get; }

        /// <summary>A Lucide icon kind.</summary>
        public string Icon { get; }

        public bool IsPrimary { get; }

        public IRelayCommand Command { get; internal set; }
    }

    /// <summary>A switch the HMI holds on or off (continuous reading, monitoring).</summary>
    public partial class DeviceSwitch : ObservableObject
    {
        public DeviceSwitch(string label, string key, string description)
        {
            Label = label;
            Key = key;
            Description = description;
        }

        public string Label { get; }

        public string Key { get; }

        public string Description { get; }

        /// <summary>What the PLC reports the switch as; null until it does.</summary>
        [ObservableProperty]
        private bool? _isOn;

        public IRelayCommand ToggleCommand { get; internal set; }
    }

    /// <summary>
    /// The Manual tab of one kind of third-party device the PLC drives (dispenser, weighing
    /// scale, vibration sensor). The device kind's page says which values it shows, which
    /// settings it takes and which buttons it has, by logical key; <see cref="IPlcDeviceService"/>
    /// maps each key to the PLC. Everything shown is what the PLC reports - nothing is
    /// assumed after a button press.
    /// </summary>
    public abstract partial class PlcDevicePanelViewModel : ObservableObject, IActivatableTab
    {
        private readonly IPlcDeviceService _service;
        private readonly IDialogService _dialog;
        private readonly SynchronizationContext _ui;
        private IDisposable _following;
        private bool _isActive;
        private bool _isSending;

        protected PlcDevicePanelViewModel(IPlcDeviceService service, IDialogService dialog)
        {
            _service = service;
            _dialog = dialog;
            _ui = SynchronizationContext.Current;
            Devices = service != null ? service.Devices : new string[0];
            _selectedDeviceIndex = Devices.Count > 0 ? 0 : -1;

            if (_service != null)
                _service.Changed += (sender, e) => OnUi(Refresh);
        }

        /// <summary>The caption above the device selector, e.g. "Select dispenser".</summary>
        public abstract string SelectorCaption { get; }

        /// <summary>What the PLC must hold for the HMI's buttons to be taken, for the notice.</summary>
        protected virtual string AutoModeNotice =>
            "The PLC's sequence controls this device (auto). Manual buttons and settings are taken only in manual.";

        public IReadOnlyList<string> Devices { get; }

        [ObservableProperty]
        private int _selectedDeviceIndex;

        partial void OnSelectedDeviceIndexChanged(int value)
        {
            if (_isActive)
            {
                _following?.Dispose();
                _following = _service?.Follow(value);
            }

            LoadSettingTexts();
            Refresh();
        }

        public bool IsAvailable => _service != null && _service.IsAvailable;

        public string UnavailableText => _service == null
            ? "This device is not part of this build."
            : _service.UnavailableReason;

        public ObservableCollection<DeviceIndicator> Indicators { get; } = new ObservableCollection<DeviceIndicator>();

        public ObservableCollection<DeviceReadingGroup> ReadingGroups { get; } = new ObservableCollection<DeviceReadingGroup>();

        public ObservableCollection<DeviceSettingGroup> SettingGroups { get; } = new ObservableCollection<DeviceSettingGroup>();

        public ObservableCollection<DeviceCommand> Commands { get; } = new ObservableCollection<DeviceCommand>();

        public ObservableCollection<DeviceSwitch> Switches { get; } = new ObservableCollection<DeviceSwitch>();

        /// <summary>The key of the link-OK flag; its state is the header chip.</summary>
        protected string ConnectedKey { get; set; }

        /// <summary>The key of the PLC's auto flag: TRUE means the sequence owns the device.</summary>
        protected string AutoModeKey { get; set; }

        protected string AlarmActiveKey { get; set; }

        protected string AlarmCodeKey { get; set; }

        protected string AlarmTextKey { get; set; }

        /// <summary>A second alarm text (the weighing scale has an operation and a detail text).</summary>
        protected string AlarmDetailKey { get; set; }

        [ObservableProperty]
        private string _linkText = string.Empty;

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private bool _isAutoMode;

        public string AutoModeText => AutoModeNotice;

        [ObservableProperty]
        private bool _hasAlarm;

        [ObservableProperty]
        private string _alarmText = string.Empty;

        /// <summary>The last action's result, for the line under the buttons.</summary>
        [ObservableProperty]
        private string _actionText = string.Empty;

        [ObservableProperty]
        private bool _actionFailed;

        /// <summary>Buttons and settings are taken: the PLC is live, the device is in manual, nothing is being sent.</summary>
        public bool CanOperate => IsAvailable && _service.IsLive && !IsAutoMode && !_isSending && SelectedDeviceIndex >= 0;

        // ==================================================================
        // Building the page (subclasses)
        // ==================================================================

        protected void AddIndicator(string label, string key, bool onIsBad = false)
        {
            if (_service != null && _service.Has(key))
                Indicators.Add(new DeviceIndicator(label, key, onIsBad));
        }

        protected void AddReadings(string title, params DeviceReading[] readings)
        {
            List<DeviceReading> mapped = readings.Where(r => _service != null && _service.Has(r.Key)).ToList();
            if (mapped.Count > 0)
                ReadingGroups.Add(new DeviceReadingGroup(title, mapped));
        }

        protected static DeviceReading Number(string label, string key, string unit = "", int decimals = 2)
        {
            return new DeviceReading(label, key, unit, decimals, false);
        }

        protected static DeviceReading Text(string label, string key)
        {
            return new DeviceReading(label, key, string.Empty, 0, true);
        }

        protected void AddSettings(string title, string applyLabel, string applyKey, params DeviceSetting[] settings)
        {
            List<DeviceSetting> mapped = settings.Where(s => _service != null && _service.Has(s.Key)).ToList();
            if (mapped.Count == 0)
                return;

            var group = new DeviceSettingGroup(title, applyLabel,
                applyKey != null && _service.Has(applyKey) ? applyKey : null, mapped);
            group.ApplyCommand = new AsyncRelayCommand(() => ApplyAsync(group), () => CanOperate);
            SettingGroups.Add(group);
        }

        protected void AddCommand(string label, string key, string icon, bool isPrimary = false)
        {
            if (_service == null || !_service.Has(key))
                return;

            var command = new DeviceCommand(label, key, icon, isPrimary);
            command.Command = new AsyncRelayCommand(() => SendAsync(label, service => service.PulseAsync(SelectedDeviceIndex, key)),
                () => CanOperate);
            Commands.Add(command);
        }

        protected void AddSwitch(string label, string key, string description)
        {
            if (_service == null || !_service.Has(key))
                return;

            var toggle = new DeviceSwitch(label, key, description);
            toggle.ToggleCommand = new AsyncRelayCommand(() =>
            {
                bool on = toggle.IsOn != true;
                return SendAsync(label + (on ? " on" : " off"), service => service.SetFlagAsync(SelectedDeviceIndex, key, on));
            }, () => CanOperate);
            Switches.Add(toggle);
        }

        /// <summary>Call at the end of the subclass constructor, once the page is built.</summary>
        protected void Complete()
        {
            LoadSettingTexts();
            Refresh();
        }

        // ==================================================================
        // Sending
        // ==================================================================

        private async Task SendAsync(string what, Func<IPlcDeviceService, Task> send)
        {
            if (!CanOperate)
                return;

            _isSending = true;
            UpdateCommands();
            try
            {
                await send(_service);
                SetAction(what + " sent to " + Devices[SelectedDeviceIndex] + ".", false);
            }
            catch (Exception ex)
            {
                SetAction(what + " could not be sent: " + ex.Message, true);
            }
            finally
            {
                _isSending = false;
                UpdateCommands();
            }
        }

        private async Task ApplyAsync(DeviceSettingGroup group)
        {
            var values = new Dictionary<string, double>();
            bool valid = true;
            foreach (DeviceSetting setting in group.Items.Where(s => s.IsVisible))
            {
                double value;
                if (!setting.TryParse(out value))
                {
                    setting.Error = "Enter a number.";
                    valid = false;
                }
                else if (value < setting.Min || value > setting.Max)
                {
                    setting.Error = "Allowed: " + setting.Range + ".";
                    valid = false;
                }
                else
                {
                    setting.Error = null;
                    values[setting.Key] = Math.Round(value, setting.Decimals);
                }
            }

            if (!valid)
            {
                SetAction(group.Title + ": correct the marked values first.", true);
                return;
            }

            if (_dialog != null && !_dialog.Confirm("Send these " + group.Title.ToLowerInvariant() + " to "
                    + Devices[SelectedDeviceIndex] + "?", group.Title))
            {
                return;
            }

            string applyKey = group.ApplyKey;
            await SendAsync(group.Title, async service =>
            {
                await service.WriteAsync(SelectedDeviceIndex, values);
                if (applyKey != null)
                    await service.PulseAsync(SelectedDeviceIndex, applyKey);
            });
        }

        private void SetAction(string text, bool failed)
        {
            ActionText = text;
            ActionFailed = failed;
        }

        // ==================================================================
        // Showing what the PLC reports
        // ==================================================================

        private void Refresh()
        {
            int device = SelectedDeviceIndex;
            bool live = IsAvailable && _service.IsLive && device >= 0;

            bool flag;
            IsConnected = live && ConnectedKey != null && _service.TryGetFlag(device, ConnectedKey, out flag) && flag;
            LinkText = !IsAvailable
                ? UnavailableText
                : !_service.IsLive
                    ? "PLC: " + _service.NotLiveReason
                    : ConnectedKey == null || !_service.Has(ConnectedKey)
                        ? "Link state not reported"
                        : IsConnected ? "Device answers" : "Device does not answer";

            IsAutoMode = live && AutoModeKey != null && _service.TryGetFlag(device, AutoModeKey, out flag) && flag;
            OnPropertyChanged(nameof(AutoModeText));

            foreach (DeviceIndicator indicator in Indicators)
                indicator.State = live && _service.TryGetFlag(device, indicator.Key, out flag) ? flag : (bool?)null;

            foreach (DeviceReading reading in ReadingGroups.SelectMany(g => g.Items))
                reading.Value = live ? Format(device, reading.Key, reading.IsText, reading.Decimals, reading.Unit) : "-";

            foreach (DeviceSetting setting in SettingGroups.SelectMany(g => g.Items))
            {
                setting.Current = live ? Format(device, setting.ReadBackKey, false, setting.Decimals, setting.Unit) : "-";
                setting.IsVisible = setting.VisibleWhenKey == null
                    || !live
                    || !_service.TryGetFlag(device, setting.VisibleWhenKey, out flag)
                    || flag == setting.VisibleWhenValue;
            }

            foreach (DeviceSwitch toggle in Switches)
                toggle.IsOn = live && _service.TryGetFlag(device, toggle.Key, out flag) ? flag : (bool?)null;

            bool alarm = live && AlarmActiveKey != null && _service.TryGetFlag(device, AlarmActiveKey, out flag) && flag;
            HasAlarm = alarm;
            AlarmText = alarm ? AlarmDescription(device) : string.Empty;

            UpdateCommands();
        }

        private string AlarmDescription(int device)
        {
            var parts = new List<string>();
            double code;
            if (AlarmCodeKey != null && _service.TryGetNumber(device, AlarmCodeKey, out code) && code != 0)
                parts.Add("Code " + code.ToString("0", CultureInfo.InvariantCulture));

            string text;
            if (AlarmTextKey != null && _service.TryGetText(device, AlarmTextKey, out text) && !string.IsNullOrWhiteSpace(text))
                parts.Add(text);
            if (AlarmDetailKey != null && _service.TryGetText(device, AlarmDetailKey, out text) && !string.IsNullOrWhiteSpace(text))
                parts.Add(text);

            return parts.Count > 0 ? string.Join(" - ", parts) : "Alarm active (no text from the PLC).";
        }

        private string Format(int device, string key, bool isText, int decimals, string unit)
        {
            if (isText)
            {
                string text;
                return _service.TryGetText(device, key, out text) && !string.IsNullOrWhiteSpace(text) ? text : "-";
            }

            double value;
            if (!_service.TryGetNumber(device, key, out value))
                return "-";

            string number = value.ToString("F" + Math.Max(0, decimals), CultureInfo.InvariantCulture);
            return unit.Length > 0 ? number + " " + unit : number;
        }

        /// <summary>The entry boxes start from the values in use, so an edit starts from what the device has.</summary>
        private void LoadSettingTexts()
        {
            int device = SelectedDeviceIndex;
            foreach (DeviceSetting setting in SettingGroups.SelectMany(g => g.Items))
            {
                double value;
                setting.Error = null;
                setting.Text = IsAvailable && device >= 0 && _service.TryGetNumber(device, setting.ReadBackKey, out value)
                    ? value.ToString("F" + Math.Max(0, setting.Decimals), CultureInfo.InvariantCulture)
                    : string.Empty;
            }
        }

        private void UpdateCommands()
        {
            OnPropertyChanged(nameof(CanOperate));
            foreach (DeviceCommand command in Commands)
                command.Command.NotifyCanExecuteChanged();
            foreach (DeviceSwitch toggle in Switches)
                toggle.ToggleCommand.NotifyCanExecuteChanged();
            foreach (DeviceSettingGroup group in SettingGroups)
                group.ApplyCommand.NotifyCanExecuteChanged();
        }

        /// <summary>The tab came on screen: the device's values are followed.</summary>
        public void Activate()
        {
            if (_isActive)
                return;

            _isActive = true;
            _following = _service?.Follow(SelectedDeviceIndex);

            // The first values arrive with the subscription; fill the entry boxes from them.
            Task.Delay(600).ContinueWith(_ => OnUi(() =>
            {
                if (SettingGroups.SelectMany(g => g.Items).All(s => string.IsNullOrEmpty(s.Text)))
                    LoadSettingTexts();
            }));
            Refresh();
        }

        /// <summary>The tab left the screen: nothing is followed.</summary>
        public void Deactivate()
        {
            if (!_isActive)
                return;

            _isActive = false;
            _following?.Dispose();
            _following = null;
        }

        private void OnUi(Action action)
        {
            if (_ui != null)
                _ui.Post(_ => action(), null);
            else
                action();
        }
    }
}
