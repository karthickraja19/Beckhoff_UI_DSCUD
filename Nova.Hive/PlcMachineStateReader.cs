using System;
using System.Threading.Tasks;
using Nova.Alarm;
using Teal.Library.Log;

namespace Nova.Hive
{
    /// <summary>
    /// The machine state as the PLC reports it (E_MachineState in the PLC project).
    /// </summary>
    /// <remarks>
    /// These numbers are the PLC's own. If E_MachineState changes in the PLC, change
    /// them here to match - this is the only place they are written down.
    /// </remarks>
    public enum PlcMachineState
    {
        Idle = 0,
        AutoRunning = 1,
        Pause = 2,
        Homing = 3,
        Alarm = 4,
        Manual = 5,
        Stop = 6,
    }

    /// <summary>
    /// The PLC's machine state, reported whenever it changes.
    /// <see cref="HiveStateMachine"/> turns that into the HIVE state. The state is an ADS
    /// notification through the HMI's shared <see cref="PlcSubscriptions"/>: the PLC pushes
    /// it when it changes, and nothing polls.
    /// </summary>
    public sealed class PlcMachineStateReader : IDisposable
    {
        private readonly PlcSubscriptions _subscriptions;
        private readonly string _symbol;
        private readonly object _sync = new object();

        /// <summary>Changes are handed on in order, off the ADS thread: the HIVE side writes files.</summary>
        private Task _delivery = Task.FromResult(0);

        private bool _started;
        private bool _hasState;
        private PlcMachineState _state;

        /// <summary>Raised on a worker thread, in order, when the PLC reports a different state.</summary>
        public event EventHandler<PlcMachineState> MachineStateChanged;

        public PlcMachineStateReader(PlcSubscriptions subscriptions, string symbol)
        {
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _symbol = symbol;
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_started)
                    return;

                _started = true;
            }

            // E_MachineState is an INT in the PLC, so two bytes.
            _subscriptions.ValueChanged += OnValueChanged;
            _subscriptions.Watch(_symbol, typeof(short));

            // Already subscribed by the time this starts: take the value the PLC last pushed.
            Report();
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (!_started)
                    return;

                _started = false;
            }

            _subscriptions.ValueChanged -= OnValueChanged;
        }

        /// <summary>The subscriptions are shared and belong to their creator, so they are left running.</summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>Runs on the ADS thread: no ADS calls, and nothing slow.</summary>
        private void OnValueChanged(object sender, string symbol)
        {
            if (string.Equals(symbol, _symbol, StringComparison.OrdinalIgnoreCase))
                Report();
        }

        private void Report()
        {
            short value;
            if (!_subscriptions.TryGet(_symbol, out value))
                return;

            var machineState = (PlcMachineState)value;

            lock (_sync)
            {
                if (!_started || (_hasState && machineState == _state))
                    return;

                _hasState = true;
                _state = machineState;
                _delivery = _delivery.ContinueWith(_ => Deliver(machineState), TaskScheduler.Default);
            }
        }

        private void Deliver(PlcMachineState machineState)
        {
            try
            {
                MachineStateChanged?.Invoke(this, machineState);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "MachineState",
                    "Handling the machine state " + machineState + " failed: " + ex.Message, ex.ToString());
            }
        }
    }
}
