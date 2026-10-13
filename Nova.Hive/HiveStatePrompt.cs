using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.Hive
{
    /// <summary>
    /// The HIVE chip's pop-ups: the state options of Audio SW DFM v5.4 slide 49, the
    /// planned downtime reasons behind them, and the badge dialog that has to come last
    /// - "Changing state requires OSS re-badge in before applying state change".
    /// </summary>
    public sealed class HiveStatePrompt : IHiveStatePrompt, IDisposable
    {
        private readonly HiveService _hive;
        private readonly IAccessControlService _access;
        private readonly PlannedDowntimeProcedures _procedures;
        private readonly IAppSession _session;
        private readonly Dispatcher _uiThread;

        public event EventHandler<HiveState> StateChanged;

        /// <param name="access">
        /// Checks the badge swiped for a state change against the access list (slide 49:
        /// Level 2/3 only). Null accepts any badge the reader delivers.
        /// </param>
        /// <param name="procedures">The planned-downtime procedures (PD-02, 04, 08, 11-15); null applies every reason directly.</param>
        /// <param name="session">Who is signed in: PD-14 and PD-15 are offered to Level 3 only.</param>
        public HiveStatePrompt(HiveService hive, IAccessControlService access = null,
            PlannedDowntimeProcedures procedures = null, IAppSession session = null)
        {
            _hive = hive;
            _access = access;
            _procedures = procedures;
            _session = session;
            _uiThread = Application.Current.Dispatcher;

            _hive.StateChanged += OnHiveStateChanged;
            _hive.AuditRoundComplete += OnAuditRoundComplete;
            _hive.DebugLimitReached += OnDebugLimitReached;
        }

        public HiveState CurrentState
        {
            get { return Describe(_hive.CurrentState, _hive.RootError); }
        }

        /// <summary>
        /// Slide 49: state options, then a reason if planned, then the badge. Options the
        /// current state does not allow (slides 8, 9 and 31) are greyed out with the reason.
        /// </summary>
        public void ShowStateChange()
        {
            List<PlannedDowntimeOption> planned = PlannedOptions();

            var options = new HiveStateDialog(
                "Now: " + DescribeCurrent(),
                _hive.CheckManualChange(HiveStateMachine.Running),
                PlannedBlocked(planned),
                _hive.CheckManualChange(HiveStateMachine.Downtime, ManualDowntime()))
            {
                Owner = Application.Current.MainWindow,
            };

            if (options.ShowDialog() != true)
                return;

            switch (options.Choice)
            {
                case HiveStateChoice.Running:
                    // Leaving dispensing pose calibration is Level 3 only (slide 75).
                    AskForBadgeAndApply(HiveStateMachine.Running, "Change to Running", null,
                        InPoseCalibration ? AccessLevel.Level3 : AccessLevel.Level2);
                    break;

                case HiveStateChoice.ManualDowntime:
                    AskForBadgeAndApply(HiveStateMachine.Downtime, "Manual downtime", ManualDowntime());
                    break;

                case HiveStateChoice.PlannedDowntime:
                    ShowPlannedDowntime(planned);
                    break;
            }
        }

        /// <summary>Slide 47: the GUI Stop button records the operator before going down.</summary>
        public void RecordStopDowntime()
        {
            // Already down - planned or not - Stop changes nothing in HIVE, so there is no
            // state change for the badge to go with.
            if (_hive.CurrentState == HiveStateMachine.Downtime)
                return;

            // Any operator may stop the machine, so this badge is not checked for a level.
            var badge = new OperatorDialog("Processing Downtime") { Owner = Application.Current.MainWindow };
            if (badge.ShowDialog() != true)
                return;

            _hive.StopPressed(badge.OperatorId, StopButtonDowntime());
        }

        public void Dispose()
        {
            _hive.StateChanged -= OnHiveStateChanged;
            _hive.AuditRoundComplete -= OnAuditRoundComplete;
            _hive.DebugLimitReached -= OnDebugLimitReached;
        }

        private bool InPoseCalibration
        {
            get
            {
                HiveError root = _hive.RootError;
                return _hive.CurrentState == HiveStateMachine.Downtime && root != null
                    && string.Equals(root.Code, HiveStateMachine.PoseCalibrationCode, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// An audit round has its units (slides 69-70): continue, go to the next GRR
        /// operator, or leave the downtime.
        /// </summary>
        private void OnAuditRoundComplete(object sender, AuditSession session)
        {
            _uiThread.BeginInvoke(new Action(() =>
            {
                var choices = new List<Choice> { new Choice("Continue test with the same operator", true) };
                if (session.HasNextOperator)
                    choices.Add(new Choice("Next operator (Operator_ID " + (session.OperatorIndex + 1) + ")"));
                choices.Add(new Choice("Exit downtime (change to Running)"));

                int picked = ChoiceDialog.Ask(session.Code + " - round complete",
                    session.Describe() + ". Continue test?", choices.ToArray());

                if (picked == 0)
                    _hive.ContinueAudit();
                else if (session.HasNextOperator && picked == 1)
                    _hive.NextAuditOperator();
                else if (picked > 0)
                    AskForBadgeAndApply(HiveStateMachine.Running, "Change to Running", null,
                        session.Code == HiveStateMachine.PoseCalibrationCode ? AccessLevel.Level3 : AccessLevel.Level2);
            }));
        }

        /// <summary>Slides 7 and 31: the debug units are used up; the OSS has to switch to Running.</summary>
        private void OnDebugLimitReached(object sender, string message)
        {
            _uiThread.BeginInvoke(new Action(() =>
            {
                MessageBox.Show(Application.Current.MainWindow, message, "HIVE - debug units",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowStateChange();
            }));
        }

        private void ShowPlannedDowntime(List<PlannedDowntimeOption> options)
        {
            var reasons = new PlannedDowntimeDialog(options) { Owner = Application.Current.MainWindow };
            if (reasons.ShowDialog() != true)
                return;

            PlannedDowntimeReason reason = reasons.Reason;

            // A reason with its own procedure walks the operator through it; the badge is
            // part of the procedure (slides 37, 66, 71-80).
            if (_procedures != null && PlannedDowntimeProcedures.HasProcedure(reason.Code))
            {
                ProcedureOutcome outcome = _procedures.Run(reason);
                if (outcome == null)
                    return;

                string refused = _hive.ChangeStateManually(HiveStateMachine.Downtime, outcome.OperatorId, outcome.Error);
                if (refused != null)
                {
                    MessageBox.Show(Application.Current.MainWindow, "The HIVE state was not changed. " + refused,
                        "HIVE", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (outcome.AfterApplied != null)
                    outcome.AfterApplied();
                return;
            }

            AskForBadgeAndApply(HiveStateMachine.Downtime, reason.Label, reason.ToError(reasons.Details),
                reason.NeedsLevel3 ? AccessLevel.Level3 : AccessLevel.Level2);
        }

        /// <summary>Every planned reason, with why it cannot be chosen from the current state.</summary>
        private List<PlannedDowntimeOption> PlannedOptions()
        {
            var options = new List<PlannedDowntimeOption>();
            foreach (PlannedDowntimeReason reason in PlannedDowntimeReason.All)
            {
                string blocked = reason.IsSelectable
                    ? _hive.CheckManualChange(HiveStateMachine.Downtime, reason.ToError())
                    : "Entered by its own procedure, not from this list.";

                if (blocked == null && reason.NeedsLevel3 && _session != null && _session.Level < AccessLevel.Level3)
                    blocked = "Only enabled when an Admin (Level 3) user is logged in.";

                options.Add(new PlannedDowntimeOption(reason, blocked));
            }

            return options;
        }

        /// <summary>Why the Planned downtime button is greyed out: no reason in the list can be chosen.</summary>
        private static string PlannedBlocked(List<PlannedDowntimeOption> options)
        {
            string first = null;
            foreach (PlannedDowntimeOption option in options)
            {
                if (option.IsEnabled)
                    return null;

                if (first == null && option.Reason.IsSelectable)
                    first = option.BlockedBecause;
            }

            return first ?? "No planned downtime reason can be chosen now.";
        }

        /// <summary>What HIVE is in now, with the downtime code - the line at the top of the options.</summary>
        private string DescribeCurrent()
        {
            HiveError error = _hive.RootError;
            switch (_hive.CurrentState)
            {
                case HiveStateMachine.Running:
                    return "Running";

                case HiveStateMachine.Idle:
                    return "Idle";

                case HiveStateMachine.Downtime:
                    if (error == null || string.IsNullOrEmpty(error.Code))
                        return "Downtime";

                    return (HiveStateMachine.IsPlanned(error.Code) ? "Planned downtime - " : "Unplanned downtime - ")
                        + error.Message + " (" + error.Code + ")";

                default:
                    return "State " + _hive.CurrentState;
            }
        }

        /// <summary>
        /// Nothing is applied until the badge is read: "No manual typing in of badge
        /// number allowed", and the badge is sent with the payload as u_id.
        /// </summary>
        /// <remarks>Slide 49: the badge must be Level 2/3 in the access list.</remarks>
        private void AskForBadgeAndApply(int state, string header, HiveError error, AccessLevel minimumLevel = AccessLevel.Level2)
        {
            var badge = new OperatorDialog(header, _access, minimumLevel) { Owner = Application.Current.MainWindow };
            if (badge.ShowDialog() != true)
                return;

            // The state may have changed while the dialogs were open - an alarm, say -
            // and the rules are checked again against the state as it is now.
            string refused = _hive.ChangeStateManually(state, badge.OperatorId, error);
            if (refused != null)
            {
                MessageBox.Show(Application.Current.MainWindow, "The HIVE state was not changed. " + refused,
                    "HIVE", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Downtime the operator chose without a planned reason. The DFM has no code of
        /// its own for it, so it goes out as Others until AME confirms one.
        /// </summary>
        private static HiveError ManualDowntime()
        {
            return HiveErrorCodes.Others("Manual downtime selected by the operator");
        }

        private static HiveError StopButtonDowntime()
        {
            return HiveErrorCodes.Others("Stop button pressed on the GUI");
        }

        private void OnHiveStateChanged(object sender, int state)
        {
            HiveState shown = Describe(state, _hive.RootError);

            // The chip is a bound property, so the change has to reach the UI thread.
            _uiThread.BeginInvoke(new Action(() => StateChanged?.Invoke(this, shown)));
        }

        /// <summary>
        /// Which chip to show. Downtime with a "PD-" code is planned downtime, and
        /// PD-100 is engineering time (DFM slide 9).
        /// </summary>
        private static HiveState Describe(int state, HiveError error)
        {
            switch (state)
            {
                case HiveStateMachine.Running:
                    return HiveState.Running;

                case HiveStateMachine.Idle:
                    return HiveState.Idle;

                case HiveStateMachine.Downtime:
                    string code = error != null ? error.Code : null;
                    if (string.IsNullOrEmpty(code) || !code.StartsWith("PD-", StringComparison.OrdinalIgnoreCase))
                        return HiveState.Downtime;

                    return code.StartsWith("PD-100", StringComparison.OrdinalIgnoreCase)
                        ? HiveState.Engineering
                        : HiveState.PlannedDowntime;

                default:
                    return HiveState.Disconnected;
            }
        }
    }
}
