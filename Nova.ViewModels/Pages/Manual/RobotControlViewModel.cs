using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// "Robot" tab of the Manual screen: the selected robot's connection and its EtherCAT I/O,
    /// from <see cref="IRobotIOService"/> - not the machine's digital IO, which does not include
    /// it. Inputs - Bools, Bytes and Words - are read only; of the outputs, the Bools can be
    /// switched and the Bytes and Words are read only. The spares are not shown.
    /// </summary>
    /// <remarks>
    /// The I/O is read only while the tab is on screen: <see cref="Activate"/> starts the
    /// service reading it (when it is an <see cref="ILiveStatus"/>), and <see cref="Deactivate"/>
    /// stops it.
    /// </remarks>
    public partial class RobotControlViewModel : ViewModelBase, IActivatableTab
    {
        private readonly IRobotIOService _robotIO;
        private IDisposable _following;
        private bool _isActive;

        // Replaced whole when another robot is selected, so a refresh on the IO thread always
        // works on one complete set.
        private Dictionary<string, RobotSignal> _inputsByName = new Dictionary<string, RobotSignal>();
        private Dictionary<string, RobotSignal> _outputsByName = new Dictionary<string, RobotSignal>();

        public ObservableCollection<string> RobotUnits { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        private string _selectedRobot;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        private bool _isConnected;

        public string StatusText => IsConnected ? "CONNECTED" : "DISCONNECTED";

        /// <summary>The Bool inputs.</summary>
        public ObservableCollection<RobotSignal> DigitalInputs { get; } = new ObservableCollection<RobotSignal>();

        /// <summary>The Byte and Word inputs.</summary>
        public ObservableCollection<RobotSignal> InputValues { get; } = new ObservableCollection<RobotSignal>();

        /// <summary>The Bool outputs, which can be switched.</summary>
        public ObservableCollection<RobotSignal> DigitalOutputs { get; } = new ObservableCollection<RobotSignal>();

        /// <summary>The Byte and Word outputs.</summary>
        public ObservableCollection<RobotSignal> OutputValues { get; } = new ObservableCollection<RobotSignal>();

        public RobotControlViewModel(IRobotIOService robotIO = null)
        {
            _robotIO = robotIO;

            if (_robotIO != null)
            {
                foreach (string robot in _robotIO.Robots)
                    RobotUnits.Add(robot);
            }

            SelectedRobot = RobotUnits.Count > 0 ? RobotUnits[0] : null;
        }

        partial void OnSelectedRobotChanged(string value) => BuildSignals();

        public void Activate()
        {
            if (_isActive || _robotIO == null)
                return;

            _isActive = true;
            _robotIO.IOChanged += OnIoChanged;
            _following = (_robotIO as ILiveStatus)?.FollowStatus();

            // What the robot is now, not what was left from the last visit.
            Refresh();
        }

        public void Deactivate()
        {
            if (!_isActive)
                return;

            _isActive = false;
            _robotIO.IOChanged -= OnIoChanged;
            _following?.Dispose();
            _following = null;
        }

        /// <summary>The rows of the selected robot's points.</summary>
        private void BuildSignals()
        {
            DigitalInputs.Clear();
            InputValues.Clear();
            DigitalOutputs.Clear();
            OutputValues.Clear();

            var snapshot = _robotIO?.Read(SelectedRobot);
            var inputs = new Dictionary<string, RobotSignal>();
            var outputs = new Dictionary<string, RobotSignal>();

            if (snapshot != null)
            {
                Add(snapshot.Inputs, inputs, DigitalInputs, InputValues);
                Add(snapshot.Outputs, outputs, DigitalOutputs, OutputValues);
            }

            _inputsByName = inputs;
            _outputsByName = outputs;
            IsConnected = snapshot != null && snapshot.IsConnected;
        }

        private static void Add(IReadOnlyList<RobotIOPointSnapshot> points, Dictionary<string, RobotSignal> byName,
            ObservableCollection<RobotSignal> bools, ObservableCollection<RobotSignal> values)
        {
            foreach (var point in points ?? new RobotIOPointSnapshot[0])
            {
                var signal = new RobotSignal(point.Name, point.Kind, point.Value);
                byName[point.Name] = signal;
                (point.Kind == RobotIOKind.Bool ? bools : values).Add(signal);
            }
        }

        /// <summary>
        /// Raised on the IO poll thread. Only scalar values are set, never the bound
        /// collections, so WPF marshals the change itself.
        /// </summary>
        private void OnIoChanged(object sender, EventArgs e) => Refresh();

        private void Refresh()
        {
            var snapshot = _robotIO?.Read(SelectedRobot);
            if (snapshot == null)
                return;

            Update(snapshot.Inputs, _inputsByName);
            Update(snapshot.Outputs, _outputsByName);
            IsConnected = snapshot.IsConnected;
        }

        private static void Update(IReadOnlyList<RobotIOPointSnapshot> points, Dictionary<string, RobotSignal> byName)
        {
            foreach (var point in points ?? new RobotIOPointSnapshot[0])
            {
                if (byName.TryGetValue(point.Name, out var signal))
                    signal.Value = point.Value;
            }
        }

        /// <summary>Switches a Bool output; the next read shows what the PLC made of it.</summary>
        [RelayCommand]
        private async Task ToggleOutput(RobotSignal signal)
        {
            if (signal == null || signal.Kind != RobotIOKind.Bool || _robotIO == null || SelectedRobot == null)
                return;

            await _robotIO.WriteOutputAsync(SelectedRobot, signal.Name, !signal.IsOn);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
                Deactivate();
            base.Dispose(disposing);
        }
    }
}
