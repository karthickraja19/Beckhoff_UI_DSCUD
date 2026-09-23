using System;
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
        private readonly Dispatcher _uiThread;

        public event EventHandler<HiveState> StateChanged;

        public HiveStatePrompt(HiveService hive)
        {
            _hive = hive;
            _uiThread = Application.Current.Dispatcher;

            _hive.StateChanged += OnHiveStateChanged;
        }

        public HiveState CurrentState
        {
            get { return Describe(_hive.CurrentState, _hive.RootError); }
        }

        /// <summary>Slide 49: state options, then a reason if planned, then the badge.</summary>
        public void ShowStateChange()
        {
            var options = new HiveStateDialog { Owner = Application.Current.MainWindow };
            if (options.ShowDialog() != true)
                return;

            switch (options.Choice)
            {
                case HiveStateChoice.Running:
                    AskForBadgeAndApply(HiveStateMachine.Running, "Change to Running", null);
                    break;

                case HiveStateChoice.Idle:
                    AskForBadgeAndApply(HiveStateMachine.Idle, "Change to Idle", null);
                    break;

                case HiveStateChoice.ManualDowntime:
                    AskForBadgeAndApply(HiveStateMachine.Downtime, "Manual downtime", ManualDowntime());
                    break;

                case HiveStateChoice.PlannedDowntime:
                    ShowPlannedDowntime();
                    break;
            }
        }

        /// <summary>Slide 47: the GUI Stop button records the operator before going down.</summary>
        public void RecordStopDowntime()
        {
            AskForBadgeAndApply(HiveStateMachine.Downtime, "Processing Downtime", StopButtonDowntime());
        }

        public void Dispose()
        {
            _hive.StateChanged -= OnHiveStateChanged;
        }

        private void ShowPlannedDowntime()
        {
            var reasons = new PlannedDowntimeDialog { Owner = Application.Current.MainWindow };
            if (reasons.ShowDialog() != true)
                return;

            PlannedDowntimeReason reason = reasons.Reason;

            var error = new HiveError
            {
                Code = reason.Code,
                Message = reason.Name,
                Detail = reason.Name,
                Severity = "warning",
                StopsMachine = true,
            };

            AskForBadgeAndApply(HiveStateMachine.Downtime, reason.Label, error);
        }

        /// <summary>
        /// Nothing is applied until the badge is read: "No manual typing in of badge
        /// number allowed", and the badge is sent with the payload as u_id.
        /// </summary>
        private void AskForBadgeAndApply(int state, string header, HiveError error)
        {
            var badge = new OperatorDialog(header) { Owner = Application.Current.MainWindow };
            if (badge.ShowDialog() != true)
                return;

            _hive.ChangeStateManually(state, badge.OperatorId, error);
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
