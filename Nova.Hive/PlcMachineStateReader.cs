using System;
using System.Threading;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;
using TwinCAT.Ads;

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
    /// Reads the PLC's machine state over ADS and reports it whenever it changes.
    /// <see cref="HiveStateMachine"/> turns that into the HIVE state.
    /// </summary>
    public sealed class PlcMachineStateReader : IDisposable
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(500);

        private readonly string _amsNetId;
        private readonly int _port;
        private readonly string _symbol;
        private readonly AdsClient _client = new AdsClient();
        private readonly object _sync = new object();

        private Timer _timer;
        private int _reading;
        private bool _hasState;
        private PlcMachineState _state;
        private bool _readFailing;

        /// <summary>Raised on the reader's thread when the PLC reports a different state.</summary>
        public event EventHandler<PlcMachineState> MachineStateChanged;

        public PlcMachineStateReader(string amsNetId, int port, string symbol)
        {
            _amsNetId = amsNetId;
            _port = port;
            _symbol = symbol;
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_timer == null)
                    _timer = new Timer(OnTimer, null, TimeSpan.Zero, CheckInterval);
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _client.Dispose();
        }

        private void OnTimer(object state)
        {
            // Skip this tick if the previous read is still running.
            if (Interlocked.Exchange(ref _reading, 1) == 1)
                return;

            try
            {
                Read();
                _readFailing = false;
            }
            catch (Exception ex)
            {
                // Logged once, not every half second.
                if (!_readFailing)
                {
                    _readFailing = true;
                    Logger.WriteLog(LogType.Error, "Hive", "MachineState",
                        "Cannot read the machine state " + _symbol + " from the PLC: " + ex.Message, ex.ToString());
                }
            }
            finally
            {
                Interlocked.Exchange(ref _reading, 0);
            }
        }

        private void Read()
        {
            if (!_client.IsConnected)
                _client.Connect(_amsNetId, _port);

            // E_MachineState is an INT in the PLC, so two bytes.
            var value = (short)_client.ReadAny(_symbol, typeof(short));
            var machineState = (PlcMachineState)value;

            if (_hasState && machineState == _state)
                return;

            _hasState = true;
            _state = machineState;

            MachineStateChanged?.Invoke(this, machineState);
        }
    }
}
