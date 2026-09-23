using System;
using System.Threading;
using Nova.Alarm;

namespace Nova.Hive
{
    /// <summary>
    /// Puts the HIVE pieces together: alarms from the PLC decide the machine state, and
    /// every state change is sent to the gateway and remembered in LatestHiveState.ini.
    /// </summary>
    /// <remarks>
    /// This is the one class the rest of the HMI talks to. What happens where:
    /// <list type="bullet">
    /// <item>the PLC reports its machine state - <see cref="HiveStateMachine"/> turns it into the HIVE state</item>
    /// <item>an alarm starts - <see cref="HiveStateMachine"/> decides whether HIVE goes down</item>
    /// <item>an alarm is cleared - the alarm is reported to HIVE with its start and end time</item>
    /// <item>a state change - <see cref="HiveClient"/> sends it, then the state is saved to the file</item>
    /// </list>
    /// Clearing the alarm does not bring HIVE back out of downtime; only the operator can,
    /// with a badge (DFM slide 8).
    /// </remarks>
    public sealed class HiveService : IDisposable
    {
        private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(1);

        private readonly HiveSettings _settings;
        private readonly AlarmMonitor _alarms;
        private readonly HiveErrorCodes _codes;
        private readonly LatestHiveStateFile _stateFile;
        private readonly HiveStateMachine _machine;
        private readonly HiveClient _client;
        private readonly PlcMachineStateReader _plcState;   // null when no symbol is configured

        private Timer _idleTimer;

        /// <summary>Raised after the state changed, with the new state (1, 2 or 5).</summary>
        public event EventHandler<int> StateChanged;

        public HiveService(HiveSettings settings, AlarmMonitor alarms)
        {
            _settings = settings;
            _alarms = alarms;
            _codes = HiveErrorCodes.Load(settings.ErrorCodeFile);
            _stateFile = new LatestHiveStateFile(settings.StateFile);
            _client = new HiveClient(settings);

            // Carry on in the state the machine was left in (sequence logic, rule #1).
            int startState = _stateFile.Read(settings.Sequence, HiveStateMachine.Idle);
            _machine = new HiveStateMachine(startState, DateTime.Now);

            if (!string.IsNullOrWhiteSpace(settings.MachineStateSymbol))
            {
                _plcState = new PlcMachineStateReader(
                    settings.PlcAmsNetId, settings.PlcAdsPort, settings.MachineStateSymbol);
            }
        }

        /// <summary>The state shown on the HIVE chip: 1 running, 2 idle, 5 downtime.</summary>
        public int CurrentState
        {
            get { return _machine.CurrentState; }
        }

        /// <summary>The alarm that put the machine into downtime, or null.</summary>
        public HiveError RootError
        {
            get { return _machine.RootError; }
        }

        /// <summary>How one PLC alarm is reported to HIVE. The pop-up shows this.</summary>
        public HiveError ErrorFor(AlarmRecord alarm)
        {
            return _codes.Find(alarm);
        }

        public void Start()
        {
            _machine.StateChanged += OnStateChanged;
            _client.MachineStateSent += OnMachineStateSent;
            _alarms.AlarmStarted += OnAlarmStarted;
            _alarms.AlarmEnded += OnAlarmEnded;

            if (_plcState != null)
            {
                _plcState.MachineStateChanged += OnMachineStateChanged;
                _plcState.Start();
            }

            _client.Start();
            _idleTimer = new Timer(_ => _machine.CheckIdle(DateTime.Now), null, IdleCheckInterval, IdleCheckInterval);
        }

        public void Stop()
        {
            _machine.StateChanged -= OnStateChanged;
            _client.MachineStateSent -= OnMachineStateSent;
            _alarms.AlarmStarted -= OnAlarmStarted;
            _alarms.AlarmEnded -= OnAlarmEnded;

            if (_plcState != null)
            {
                _plcState.MachineStateChanged -= OnMachineStateChanged;
                _plcState.Stop();
            }

            if (_idleTimer != null)
            {
                _idleTimer.Dispose();
                _idleTimer = null;
            }

            _client.Stop();
        }

        public void Dispose()
        {
            Stop();

            if (_plcState != null)
                _plcState.Dispose();

            _client.Dispose();
        }

        /// <summary>Call when a unit enters the machine: HIVE goes from idle back to running.</summary>
        public void UnitEntered()
        {
            _machine.UnitEntered(DateTime.Now);
        }

        /// <summary>
        /// The operator closed the alarm pop-up after swiping a badge. Sends the state
        /// again (5 to 5) with the badge and the wording the operator confirmed.
        /// </summary>
        public void AlarmAcknowledged(HiveError error, string errorDetail, string repairActionCode, string operatorId)
        {
            HiveError confirmed = error.Copy();
            confirmed.Detail = errorDetail;

            // The DFM appends the repair action to the error code, e.g. M03CVSO-01-09.
            if (!string.IsNullOrWhiteSpace(repairActionCode))
                confirmed.Code = error.Code + repairActionCode;

            _machine.AlarmAcknowledged(confirmed, operatorId, DateTime.Now);
        }

        /// <summary>
        /// The operator changed the state by hand from the HIVE chip (level 2/3 with a
        /// badge). This is the only way out of downtime.
        /// </summary>
        public void ChangeStateManually(int newState, string operatorId, HiveError plannedDowntime = null)
        {
            _machine.ChangeManually(newState, operatorId, DateTime.Now, plannedDowntime);
        }

        private void OnMachineStateChanged(object sender, PlcMachineState state)
        {
            _machine.MachineStateChanged(state, DateTime.Now);
        }

        private void OnAlarmStarted(object sender, AlarmRecord alarm)
        {
            _machine.AlarmStarted(_codes.Find(alarm), alarm.StartTime);
        }

        private void OnAlarmEnded(object sender, AlarmRecord alarm)
        {
            if (alarm.EndTime.HasValue)
                _client.SendError(_codes.Find(alarm), alarm.StartTime, alarm.EndTime.Value);
        }

        private void OnStateChanged(object sender, HiveStateChange change)
        {
            _client.Send(change);
            StateChanged?.Invoke(this, change.State);
        }

        private void OnMachineStateSent(object sender, HiveStateChange change)
        {
            // Only after the gateway accepted it, so the file cannot claim a state HIVE never got.
            _stateFile.Save(_settings.Sequence, change.State);
        }
    }
}
