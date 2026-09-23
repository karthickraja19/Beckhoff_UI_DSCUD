using System;

namespace Nova.Hive
{
    /// <summary>One HIVE state change, which becomes one machine_state payload.</summary>
    public sealed class HiveStateChange
    {
        public int State { get; set; }

        public int PreviousState { get; set; }

        public DateTime Time { get; set; }

        /// <summary>Error that caused the downtime. Only filled in for state 5.</summary>
        public HiveError Error { get; set; }

        /// <summary>Badge of the operator, only when the state was changed by hand.</summary>
        public string OperatorId { get; set; }
    }

    /// <summary>
    /// Decides the HIVE machine state (Audio SW DFM v5.4, slides 7 and 8).
    /// </summary>
    /// <remarks>
    /// The PLC says what state the machine is in; these rules turn that, and the alarms,
    /// into the HIVE state:
    /// <list type="bullet">
    /// <item>the PLC in auto means HIVE is running</item>
    /// <item>90 seconds after the machine stops processing, HIVE goes to idle</item>
    /// <item>an alarm that stops the machine, while running or idle, goes to downtime</item>
    /// <item>another alarm while already down changes nothing - no second payload</item>
    /// <item>the PLC stopping with no alarm to explain it also goes to downtime</item>
    /// <item>leaving downtime is only possible by hand, by a level 2/3 operator with a badge</item>
    /// <item>resetting the alarm or pressing Start does not change the HIVE state</item>
    /// </list>
    /// Nothing in here talks to the gateway or the screen, so the rules can be read on
    /// their own. Times are passed in rather than read from the clock, so they can be tested.
    /// </remarks>
    public sealed class HiveStateMachine
    {
        public const int Running = 1;
        public const int Idle = 2;
        public const int Downtime = 5;

        /// <summary>Slide 7: "No units have processed in the last 90 seconds".</summary>
        private static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(90);

        /// <summary>
        /// How long the machine may sit in alarm or stop before downtime is reported
        /// without an error code. The alarm that explains it normally arrives within a
        /// moment, and its code is the one HIVE wants.
        /// </summary>
        private static readonly TimeSpan WaitForAlarm = TimeSpan.FromSeconds(3);

        private DateTime _lastUnitTime;
        private PlcMachineState _machineState;
        private DateTime? _stoppedWithoutAlarmSince;

        /// <summary>Raised whenever the state changes and a payload has to be sent.</summary>
        public event EventHandler<HiveStateChange> StateChanged;

        public HiveStateMachine(int startState, DateTime now)
        {
            CurrentState = startState;
            _lastUnitTime = now;
        }

        public int CurrentState { get; private set; }

        /// <summary>The alarm that put the machine into downtime, or null.</summary>
        public HiveError RootError { get; private set; }

        /// <summary>
        /// The PLC reported a different machine state. Auto means HIVE is running;
        /// alarm and stop mean downtime; everything else is "not processing", which
        /// becomes idle once the 90 seconds are up.
        /// </summary>
        public void MachineStateChanged(PlcMachineState state, DateTime time)
        {
            _machineState = state;

            // Downtime is only left by hand (slide 8), so the machine coming back by
            // itself - reset, Start pressed - does not change the HIVE state.
            if (CurrentState == Downtime)
                return;

            if (state == PlcMachineState.Alarm || state == PlcMachineState.Stop)
            {
                // Wait a moment for the alarm that explains it: its code is what HIVE
                // wants on the state 5 payload.
                if (_stoppedWithoutAlarmSince == null)
                    _stoppedWithoutAlarmSince = time;

                return;
            }

            _stoppedWithoutAlarmSince = null;

            if (state == PlcMachineState.AutoRunning)
            {
                _lastUnitTime = time;
                ChangeTo(Running, time, null, null);
            }

            // Idle, pause, homing and manual are all "not processing units": HIVE stays
            // as it is until the 90 seconds in CheckIdle are up.
        }

        /// <summary>An alarm appeared in the PLC's alarm list.</summary>
        public void AlarmStarted(HiveError error, DateTime time)
        {
            // The alarm explains the stop, so the fallback below is no longer needed.
            _stoppedWithoutAlarmSince = null;

            // A warning does not stop production, so it does not change the HIVE state.
            if (!error.StopsMachine)
                return;

            // Already down: the operator sees the new alarm in the pop-up, but HIVE keeps
            // the alarm that caused the downtime and gets no second payload.
            if (CurrentState == Downtime)
                return;

            RootError = error;
            ChangeTo(Downtime, time, error, null);
        }

        /// <summary>A unit entered the machine.</summary>
        public void UnitEntered(DateTime time)
        {
            _lastUnitTime = time;

            if (CurrentState == Idle)
                ChangeTo(Running, time, null, null);
        }

        /// <summary>
        /// Called about once a second. Moves to idle 90 seconds after the machine stopped
        /// processing, and reports downtime for a machine that stopped with no alarm.
        /// </summary>
        public void CheckIdle(DateTime now)
        {
            if (CurrentState == Downtime)
                return;

            // The machine is in alarm or stop, but no alarm arrived to explain it.
            // Report the downtime anyway rather than leaving HIVE showing production.
            if (_stoppedWithoutAlarmSince.HasValue && now - _stoppedWithoutAlarmSince.Value >= WaitForAlarm)
            {
                _stoppedWithoutAlarmSince = null;
                RootError = HiveErrorCodes.Others("The machine stopped without an alarm");
                ChangeTo(Downtime, now, RootError, null);
                return;
            }

            // While the PLC is in auto the machine is processing, so the 90 seconds only
            // start once it leaves auto. When the PLC reports units, call UnitEntered
            // instead and this can follow the units themselves.
            if (_machineState == PlcMachineState.AutoRunning)
                _lastUnitTime = now;

            if (CurrentState == Running && now - _lastUnitTime >= IdleAfter)
                ChangeTo(Idle, now, null, null);
        }

        /// <summary>
        /// The operator changed the state by hand (level 2/3 with a badge). This is the
        /// only way out of downtime.
        /// </summary>
        /// <param name="plannedDowntime">The "PD-" reason when going to planned downtime, otherwise null.</param>
        public void ChangeManually(int newState, string operatorId, DateTime time, HiveError plannedDowntime = null)
        {
            if (newState == Running)
                _lastUnitTime = time;   // do not fall straight back to idle

            RootError = newState == Downtime ? plannedDowntime : null;
            ChangeTo(newState, time, plannedDowntime, operatorId);
        }

        /// <summary>
        /// The operator closed the alarm pop-up after swiping a badge. The DFM records
        /// this as a change from state 5 to state 5, carrying the badge and the wording
        /// the operator confirmed.
        /// </summary>
        public void AlarmAcknowledged(HiveError error, string operatorId, DateTime time)
        {
            RootError = error;
            ChangeTo(Downtime, time, error, operatorId, alwaysSend: true);
        }

        private void ChangeTo(int newState, DateTime time, HiveError error, string operatorId, bool alwaysSend = false)
        {
            // No repeat payloads: HIVE only wants a message when something really changed.
            if (newState == CurrentState && !alwaysSend)
                return;

            var change = new HiveStateChange
            {
                State = newState,
                PreviousState = CurrentState,
                Time = time,
                // Error information belongs on state 5 payloads only.
                Error = newState == Downtime ? error : null,
                OperatorId = operatorId,
            };

            CurrentState = newState;

            StateChanged?.Invoke(this, change);
        }
    }
}
