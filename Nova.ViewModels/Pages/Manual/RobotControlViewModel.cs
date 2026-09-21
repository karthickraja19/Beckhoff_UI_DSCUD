using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware.Enums;
using Nova.Abstractions.Hardware.Interfaces;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// "Robot" tab of the Manual screen. Combines an illustrative digital I/O board
    /// with the shared console panel (from <see cref="ConsoleDeviceViewModel"/>) used
    /// to select a command and send it over the selected robot's Communication
    /// channel (V3: cmbRobot + btnRBTtrigger). The robot list + TCP transport come
    /// from Robots.json via <see cref="ICommDeviceService"/>; the digital I/O board is
    /// still illustrative until robot IO is wired.
    /// </summary>
    public class RobotControlViewModel : ConsoleDeviceViewModel
    {
        private readonly Dictionary<string, INetworkProtocol> _robotProtocols =
            new Dictionary<string, INetworkProtocol>();

        private readonly IDigitalIOService _io;
        private readonly Dictionary<int, RobotSignal> _inputsByBit = new Dictionary<int, RobotSignal>();
        private readonly Dictionary<int, RobotSignal> _outputsByBit = new Dictionary<int, RobotSignal>();

        public override string SelectorCaption => "Select CMD :";
        public override string TriggerLabel => "SEND COMMAND";
        public override string TriggerIcon => "Bot";

        public ObservableCollection<string> RobotUnits { get; }

        private string _selectedRobot;
        public string SelectedRobot
        {
            get => _selectedRobot;
            set => SetProperty(ref _selectedRobot, value);
        }

        public ObservableCollection<RobotSignal> DigitalInputs { get; }
        public ObservableCollection<RobotSignal> DigitalOutputs { get; }

        public RobotControlViewModel(ICommDeviceService comm, IDigitalIOService io = null)
        {
            _io = io;
            RobotUnits = new ObservableCollection<string>();
            var robots = comm?.GetDevices(CommDeviceKind.Robot);
            if (robots != null && robots.Count > 0)
            {
                foreach (var r in robots)
                {
                    RobotUnits.Add(r.Name);
                    if (r.Protocol != null)
                        _robotProtocols[r.Name] = r.Protocol;
                }
            }
            else
            {
                foreach (var name in Enum.GetNames(typeof(Robots)))
                    RobotUnits.Add(name);
            }
            _selectedRobot = RobotUnits.Count > 0 ? RobotUnits[0] : null;

            // Robot IO is not a transport concern: it is wired to the same digital input
            // bank as everything else, at the bit numbers the enums carry
            // (RobotInputs from 68, RobotOutputs from 21). V3's RbtInputState /
            // RbtOutputState read both lists with DIDO.ReadDI — outputs included, since
            // those members are feedback bits — so both are read from the input bank here.
            DigitalInputs = new ObservableCollection<RobotSignal>();
            foreach (RobotInputs value in Enum.GetValues(typeof(RobotInputs)))
            {
                var signal = new RobotSignal(value.ToString());
                DigitalInputs.Add(signal);
                _inputsByBit[(int)value] = signal;
            }

            DigitalOutputs = new ObservableCollection<RobotSignal>();
            foreach (RobotOutputs value in Enum.GetValues(typeof(RobotOutputs)))
            {
                var signal = new RobotSignal(value.ToString());
                DigitalOutputs.Add(signal);
                _outputsByBit[(int)value] = signal;
            }

            if (_io != null)
            {
                RefreshSignals();
                _io.IOChanged += OnIoChanged;
            }

            // Command payloads (the send-command presets).
            Devices.Add(new ConsoleDeviceItem("AUTO_MODE", "", "AUTO_MODE, 1.0, Auto_mode_Selected, IN, END"));
            Devices.Add(new ConsoleDeviceItem("MANUAL_MODE", "", "MANUAL_MODE, 1.0, END"));
            Devices.Add(new ConsoleDeviceItem("HOME", "", "HOME_REQ, 1, END"));
            Devices.Add(new ConsoleDeviceItem("PICK", "", "PICK_REQ, POS=1, END"));
            Devices.Add(new ConsoleDeviceItem("PLACE", "", "PLACE_REQ, POS=1, END"));
            SelectedDevice = Devices[0];   // loads SendCommand via OnDeviceSelected

            if (_robotProtocols.Count == 0)
            {
                // Dev fallback (no config): demo console.
                IsConnected = true;
                Console.Add(new CameraConsoleLine("RBT_INIT_REQ..."));
                Console.Add(new CameraConsoleLine("ACK INIT_SUCCESS", isAck: true));
            }
        }

        private INetworkProtocol RobotProtocol
            => SelectedRobot != null && _robotProtocols.TryGetValue(SelectedRobot, out var p) ? p : null;

        protected override async Task<bool> DoConnectAsync()
        {
            var p = RobotProtocol;
            return p == null ? true : await p.ConnectAsync();
        }

        protected override async Task<string> DoTriggerAsync()
        {
            var p = RobotProtocol;
            return p == null ? null : await p.SendAndReceiveAsync(SendCommand);
        }

        protected override Task DoDisconnectAsync()
        {
            RobotProtocol?.Disconnect();
            return Task.CompletedTask;
        }

        protected override void OnTrigger()
        {
            Console.Add(new CameraConsoleLine("SEND " + (SelectedDevice?.Name ?? "CMD")));
            Console.Add(new CameraConsoleLine("ACK DONE", isAck: true));
        }

        /// <summary>
        /// Raised on the IO poll thread. Only scalar RobotSignal.IsOn values are set,
        /// never the bound collections, so WPF marshals the change itself.
        /// </summary>
        private void OnIoChanged(object sender, EventArgs e) => RefreshSignals();

        private void RefreshSignals()
        {
            var points = _io.ReadInputs();
            if (points == null)
                return;

            foreach (var point in points)
            {
                RobotSignal signal;
                if (_inputsByBit.TryGetValue(point.Bit, out signal))
                    signal.IsOn = point.IsOn;
                if (_outputsByBit.TryGetValue(point.Bit, out signal))
                    signal.IsOn = point.IsOn;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed && _io != null)
                _io.IOChanged -= OnIoChanged;
            base.Dispose(disposing);
        }
    }
}
