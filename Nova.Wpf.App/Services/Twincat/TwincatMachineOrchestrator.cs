using System;
using System.Threading;
using System.Threading.Tasks;
using Nova.Abstractions.Machine.Enums;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Nova.Hive;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// The indicator bar's Start / Pause / Stop buttons and the Setting page's Manual tab's manual mode, on
    /// the PLC's mode selection (FB_ModeSelection, behind MAIN.modecall). The PLC owns the
    /// machine state: a button only pulses the PLC's push-button BOOL, and the state the
    /// buttons follow is the PLC's own E_MachineState, pushed through the shared subscriptions.
    /// </summary>
    /// <remarks>
    /// The PLC reacts to a button's rising edge, so a press writes TRUE and, 200 ms (20 PLC
    /// cycles) later, FALSE. The writes are made one at a time, in order, on a worker thread,
    /// so a PLC that does not answer never holds up the screen.
    ///
    /// Manual mode is a level, not a pulse: TRUE while the Manual tab is on screen. It is
    /// written again each time the PLC comes back (a PLC restart clears it), and cleared
    /// when the HMI closes.
    /// </remarks>
    public sealed class TwincatMachineOrchestrator : IMachineOrchestrator
    {
        private static readonly TimeSpan PulseLength = TimeSpan.FromMilliseconds(200);

        private readonly TwincatPlcConnection _plc;
        private readonly PlcSubscriptions _subscriptions;
        private readonly IDialogService _dialogs;
        private readonly IHomingService _homing;
        private readonly string _stateSymbol;
        private readonly string _startSymbol;
        private readonly string _pauseSymbol;
        private readonly string _stopSymbol;
        private readonly string _manualSymbol;
        private readonly SynchronizationContext _uiContext;
        private readonly object _sync = new object();

        private Task _writes = Task.CompletedTask;   // the PLC writes waiting to be made, in order
        private MachineStatus _status = MachineStatus.Waiting;
        private volatile bool _manualMode;
        private int _manualHolders;                  // tabs on screen that hold manual mode
        private bool _started;

        public event Action<MachineStatus> OnMachineStatusChanged;

        /// <param name="stateSymbol">The PLC's E_MachineState (App.config PlcMachineStateSymbol).</param>
        /// <param name="startSymbol">BOOL pulsed by Start, and by resume from pause.</param>
        /// <param name="pauseSymbol">BOOL pulsed by Pause.</param>
        /// <param name="stopSymbol">BOOL pulsed by Stop.</param>
        /// <param name="manualSymbol">BOOL held TRUE while the Setting page's Manual tab is on screen.</param>
        public TwincatMachineOrchestrator(
            TwincatPlcConnection plc, PlcSubscriptions subscriptions, IDialogService dialogs, IHomingService homing,
            string stateSymbol, string startSymbol, string pauseSymbol, string stopSymbol, string manualSymbol)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _homing = homing ?? throw new ArgumentNullException(nameof(homing));
            _stateSymbol = stateSymbol ?? string.Empty;
            _startSymbol = startSymbol ?? string.Empty;
            _pauseSymbol = pauseSymbol ?? string.Empty;
            _stopSymbol = stopSymbol ?? string.Empty;
            _manualSymbol = manualSymbol ?? string.Empty;

            // Resolved on the UI thread at shell construction, so status events and dialogs
            // raised from another thread are marshalled back through this context.
            _uiContext = SynchronizationContext.Current;
        }

        /// <summary>The PLC's machine state; Waiting while the PLC is not live or has not reported it.</summary>
        public MachineStatus CurrentStatus
        {
            get { lock (_sync) return _status; }
        }

        /// <summary>The PLC's homing-done bit; false while it is not wired (PlcHomingDoneSymbol).</summary>
        public bool IsHomingDone => _homing.ReportsHomed && _homing.IsHomed;

        public bool IsAutoRunning
        {
            get
            {
                MachineStatus status = CurrentStatus;
                return status == MachineStatus.Running || status == MachineStatus.Pause;
            }
        }

        /// <summary>Begins following the PLC's machine state, for the life of the HMI.</summary>
        public void Start()
        {
            lock (_sync)
            {
                if (_started)
                    return;

                _started = true;
            }

            _subscriptions.ValueChanged += OnValueChanged;
            _subscriptions.LiveChanged += OnLiveChanged;

            // E_MachineState is an INT in the PLC, so two bytes.
            _subscriptions.Watch(_stateSymbol, typeof(short));
            UpdateStatus();
        }

        /// <summary>Start, or resume when paused: the PLC's START button.</summary>
        public Task StartAutoAsync()
        {
            Pulse(_startSymbol, "Start", "PlcStartSymbol");
            return Task.CompletedTask;
        }

        public void Pause()
        {
            Pulse(_pauseSymbol, "Pause", "PlcPauseSymbol");
        }

        public void Resume()
        {
            Pulse(_startSymbol, "Start", "PlcStartSymbol");
        }

        public void Stop()
        {
            Pulse(_stopSymbol, "Stop", "PlcStopSymbol");
        }

        /// <summary>Raises the PLC's home command bit, as the Manual tab's Homing button does.</summary>
        public Task StartHomingAsync()
        {
            if (IsAutoRunning || _homing.IsHoming)
                return Task.CompletedTask;

            if (!_homing.IsAvailable)
            {
                Warn(_homing.UnavailableReason, "Homing");
                return Task.CompletedTask;
            }

            _homing.StartHoming();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Nothing to clear in the HMI: the alarms and the machine state are the PLC's,
        /// cleared by its RESET.
        /// </summary>
        public void ResetAlarms()
        {
        }

        /// <summary>
        /// Each tab that needs manual mode (Manual, Calibration, Recipe > Gantry) holds it while on screen:
        /// TRUE is written when the first one takes it, FALSE when the last one lets it go.
        /// So moving from one of those tabs to the other never clears the bit - which would
        /// send the PLC back to IDLE and ask for homing again.
        /// </summary>
        public void SetManualMode(bool on)
        {
            if (_manualSymbol.Length == 0)
                return;

            lock (_sync)
            {
                if (on)
                    _manualHolders++;
                else if (_manualHolders > 0)
                    _manualHolders--;

                if ((_manualHolders > 0) == _manualMode)
                    return;

                _manualMode = _manualHolders > 0;
                on = _manualMode;
            }

            // Not live: written as soon as the PLC is back (OnLiveChanged).
            if (_subscriptions.IsLive)
                QueueWrite(() => WriteManualMode(on), "Manual mode");
        }

        /// <summary>
        /// Takes the PLC out of manual mode as the HMI closes, if a tab put it there.
        /// Waits briefly for the writes already queued, so this one lands last.
        /// </summary>
        public void ReleaseManualModeOnExit()
        {
            if (_manualSymbol.Length == 0 || !_manualMode)
                return;

            _manualMode = false;
            lock (_sync)
            {
                _manualHolders = 0;
            }

            try
            {
                Task queued;
                lock (_sync)
                {
                    queued = _writes;
                }

                queued.Wait(TimeSpan.FromSeconds(1));
                WriteManualMode(false);
            }
            catch (Exception ex)
            {
                LogError("Could not take the PLC out of manual mode on exit", ex);
            }
        }

        /// <summary>Sets the button's BOOL TRUE, then FALSE: the PLC acts on the rising edge.</summary>
        private void Pulse(string symbol, string button, string settingKey)
        {
            if (symbol.Length == 0)
            {
                Warn(button + " is not wired to the PLC: " + settingKey + " is not set in App.config.", button);
                return;
            }

            QueueWrite(() =>
            {
                Write(symbol, true);
                Thread.Sleep(PulseLength);
                Write(symbol, false);
            }, button);
        }

        private void WriteManualMode(bool on)
        {
            Write(_manualSymbol, on);
        }

        private void Write(string symbol, bool value)
        {
            if (!_plc.IsConnected)
                throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");

            _plc.WriteSymbol(symbol, value);
        }

        /// <summary>Runs the write after every write queued before it, on a worker thread.</summary>
        private void QueueWrite(Action write, string what)
        {
            lock (_sync)
            {
                _writes = _writes.ContinueWith(_ =>
                {
                    try
                    {
                        write();
                    }
                    catch (Exception ex)
                    {
                        LogError(what + " could not be written to the PLC", ex);
                        Warn(what + " could not be sent to the PLC: " + ex.Message, what);
                    }
                }, TaskScheduler.Default);
            }
        }

        /// <summary>Runs on the ADS thread: no ADS calls, and nothing slow.</summary>
        private void OnValueChanged(object sender, string symbol)
        {
            if (string.Equals(symbol, _stateSymbol, StringComparison.OrdinalIgnoreCase))
                UpdateStatus();
        }

        /// <summary>Runs on the ADS or a worker thread.</summary>
        private void OnLiveChanged(object sender, EventArgs e)
        {
            UpdateStatus();

            // The PLC is back - after a restart its manual-mode bit is FALSE again, and after
            // an HMI that closed without clearing it, TRUE. Put it to what the screen shows.
            if (_subscriptions.IsLive && _manualSymbol.Length > 0)
            {
                bool manual = _manualMode;
                QueueWrite(() => WriteManualMode(manual), "Manual mode");
            }
        }

        private void UpdateStatus()
        {
            MachineStatus status = ReadStatus();
            lock (_sync)
            {
                if (status == _status)
                    return;

                _status = status;
            }

            Action<MachineStatus> handler = OnMachineStatusChanged;
            if (handler == null)
                return;

            if (_uiContext != null)
                _uiContext.Post(_ => handler(status), null);
            else
                handler(status);
        }

        /// <summary>The PLC's E_MachineState as the HMI's status; Waiting with no value from the PLC.</summary>
        private MachineStatus ReadStatus()
        {
            short value;
            if (!_subscriptions.TryGet(_stateSymbol, out value))
                return MachineStatus.Waiting;

            switch ((PlcMachineState)value)
            {
                case PlcMachineState.Idle: return MachineStatus.Idle;
                case PlcMachineState.AutoRunning: return MachineStatus.Running;
                case PlcMachineState.Pause: return MachineStatus.Pause;
                case PlcMachineState.Homing: return MachineStatus.Homing;
                case PlcMachineState.Alarm: return MachineStatus.Alarm;
                case PlcMachineState.Manual: return MachineStatus.Manual;
                case PlcMachineState.Stop: return MachineStatus.Stop;
                default: return MachineStatus.Waiting;
            }
        }

        /// <summary>Shown without waiting: this can be a worker thread.</summary>
        private void Warn(string message, string title)
        {
            if (_uiContext != null)
                _uiContext.Post(_ => _dialogs.ShowWarning(message, title), null);
            else
                _dialogs.ShowWarning(message, title);
        }

        private static void LogError(string message, Exception ex)
        {
            Logger.WriteLog(LogType.Error, "PLC", "MachineButtons", message + ": " + ex.Message, ex.ToString());
        }
    }
}
