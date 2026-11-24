using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The HIVE Control tab of the Setting page (S2.4 Extra Log for HIVE Remote
    /// Qualification, page 5): Disable HIVE and the 24-hour dry run function module.
    /// </summary>
    /// <remarks>
    /// "HIVE IS LINE CRITICAL. HIVE CANNOT BE DISABLED WITHOUT AME APPROVAL IN ADVANCE."
    /// Disabling is Level 3 only, for one of the two reasons S2.4 allows, and needs a second
    /// approver's badge; both names and the reason are recorded. Re-enabling needs Level 3.
    /// </remarks>
    public partial class HiveControlTabViewModel : SettingsTabBase
    {
        private readonly IHiveControl _hive;
        private readonly IBadgePrompt _badges;
        private readonly IDialogService _dialog;
        private readonly IAppSession _session;
        private readonly IAccessLog _log;

        public HiveControlTabViewModel(IHiveControl hive, IBadgePrompt badges, IDialogService dialog, IAppSession session, IAccessLog log)
        {
            _hive = hive;
            _badges = badges;
            _dialog = dialog;
            _session = session;
            _log = log;

            DisableCommand = new RelayCommand(Disable, () => CanEditCritical && IsAvailable && IsHiveEnabled && !string.IsNullOrEmpty(DisableReason));
            EnableCommand = new RelayCommand(Enable, () => CanEditCritical && IsAvailable && !IsHiveEnabled);
            ToggleDryRunCommand = new RelayCommand(ToggleDryRun, () => CanEditCritical);

            if (_hive != null)
                _hive.Changed += (sender, e) => OnUi(Refresh);

            Refresh();
        }

        public override string Title => "HIVE Control";

        /// <summary>S2.4: "HIVE may only be disabled under the following conditions".</summary>
        public IReadOnlyList<string> DisableReasons { get; } = new[]
        {
            "Network anomalies occur",
            "HIVE setup configuration remains incomplete",
        };

        public bool IsAvailable => _hive != null && _hive.IsAvailable;

        public bool IsHiveEnabled => _hive != null && _hive.IsEnabled;

        public string HiveStatus
        {
            get
            {
                if (_hive == null)
                    return "HIVE reporting is not available on this machine.";
                if (!_hive.IsAvailable)
                    return _hive.DisabledNote;
                return _hive.IsEnabled ? "Enabled - every state change is sent to the HIVE gateway." : "DISABLED. " + _hive.DisabledNote;
            }
        }

        public bool IsDryRunEnabled => _hive != null && _hive.IsDryRunEnabled;

        public string DryRunStatus => IsDryRunEnabled
            ? "Enabled - for FAT remote qualification only. It is reset to Disabled every time the software starts."
            : DryRunUnavailable == null ? "Disabled (default)."
            : "Disabled (default). It cannot be enabled on this machine: " + DryRunUnavailable;

        /// <summary>Why the dry run cannot be enabled (no SI code, the DLLs are not there); null when it can.</summary>
        private string DryRunUnavailable => _hive != null ? _hive.DryRunUnavailableReason : null;

        public string DryRunButtonText => IsDryRunEnabled ? "Disable 24-hour dry run" : "Enable 24-hour dry run";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DisableCommand))]
        private string _disableReason;

        public IRelayCommand DisableCommand { get; }
        public IRelayCommand EnableCommand { get; }
        public IRelayCommand ToggleDryRunCommand { get; }

        public string AccessHint => CanEditCritical
            ? "Level 3. Disabling HIVE also needs a second approval, and CM/BUD approval must be obtained first."
            : "Only enabled when an Admin (Level 3) user is logged in.";

        protected override void OnLevelChanged()
        {
            OnPropertyChanged(nameof(AccessHint));
            NotifyCommands();
        }

        public override void OnShown()
        {
            Refresh();
        }

        private void Refresh()
        {
            OnPropertyChanged(nameof(IsAvailable));
            OnPropertyChanged(nameof(IsHiveEnabled));
            OnPropertyChanged(nameof(HiveStatus));
            OnPropertyChanged(nameof(IsDryRunEnabled));
            OnPropertyChanged(nameof(DryRunStatus));
            OnPropertyChanged(nameof(DryRunButtonText));
            NotifyCommands();
        }

        private void Disable()
        {
            if (!CanEditCritical || _hive == null)
                return;

            if (_dialog != null && !_dialog.Confirm(
                    "HIVE is line critical. Disable HIVE because \"" + DisableReason + "\"?\n\n"
                    + "Prior approval must be obtained from CM/BUD. A second approver swipes their badge next.",
                    "Disable HIVE"))
            {
                return;
            }

            AccessCheckResult approver = _badges != null ? _badges.RequestSecondApproval("Disable HIVE") : null;
            if (approver == null)
                return;

            string error;
            if (!_hive.SetEnabled(false, DisableReason, OperatorName, approver.Name, out error))
            {
                _dialog?.ShowError(error, "Disable HIVE");
                return;
            }

            Record("disabled HIVE (" + DisableReason + "), approved by " + approver.Name);
            DisableReason = null;
            Refresh();
        }

        private void Enable()
        {
            if (!CanEditCritical || _hive == null)
                return;

            string error;
            if (!_hive.SetEnabled(true, null, OperatorName, null, out error))
            {
                _dialog?.ShowError(error, "Enable HIVE");
                return;
            }

            Record("enabled HIVE");
            Refresh();
        }

        private void ToggleDryRun()
        {
            if (!CanEditCritical || _hive == null)
                return;

            bool enable = !_hive.IsDryRunEnabled;
            if (enable && DryRunUnavailable != null)
            {
                _dialog?.ShowWarning("The 24-hour dry run cannot be enabled: " + DryRunUnavailable, "24-Hour Dry Run");
                return;
            }

            if (enable && _dialog != null && !_dialog.Confirm(
                    "Enable the 24-hour dry run? It is for FAT remote qualification only.", "24-Hour Dry Run"))
            {
                return;
            }

            _hive.SetDryRun(enable, OperatorName);
            Record((enable ? "enabled" : "disabled") + " the 24-hour dry run");
            Refresh();
        }

        private string OperatorName => _session != null ? _session.UserName : string.Empty;

        private void Record(string action)
        {
            if (_log != null && _session != null)
                _log.Action(_session.UserName, (int)_session.Level, action);
        }

        private void NotifyCommands()
        {
            DisableCommand.NotifyCanExecuteChanged();
            EnableCommand.NotifyCanExecuteChanged();
            ToggleDryRunCommand.NotifyCanExecuteChanged();
        }
    }
}
