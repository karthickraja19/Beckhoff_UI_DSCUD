using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The Early Warning tab of the Setting page (S17 Early Warning For Identifying Common
    /// Defect Factors X3, the "Test" page).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Test info: a terminal name and a serial number to test with. Test builds the
    /// request, sends it to MES and shows both bodies, read only. Reset clears the two fields.</item>
    /// <item>"Disable the early warning for common defect factors": unticked (enabled) at every
    /// start; changing it needs the CM DRI's card, checked with MES. Disabled silences the
    /// pop-up and the buzzer only - MES is still asked after every unit.</item>
    /// </list>
    /// </remarks>
    public partial class EarlyWarningTabViewModel : SettingsTabBase
    {
        private readonly IEarlyWarning _warning;
        private readonly IDialogService _dialog;
        private readonly IAppSession _session;
        private readonly IAccessLog _log;

        public EarlyWarningTabViewModel(IEarlyWarning warning, IDialogService dialog, IAppSession session, IAccessLog log)
        {
            _warning = warning;
            _dialog = dialog;
            _session = session;
            _log = log;

            TestCommand = new AsyncRelayCommand(TestAsync, () => IsAvailable && !IsTesting);
            ResetCommand = new RelayCommand(Reset);

            if (_warning != null)
            {
                _terminalName = _warning.TerminalName ?? string.Empty;
                _warning.Changed += (sender, e) => OnUi(() =>
                {
                    OnPropertyChanged(nameof(IsDisabled));
                    OnPropertyChanged(nameof(StateText));
                });
            }
        }

        public override string Title => "Early Warning";

        /// <summary>False when the machine has no early warning (no service registered).</summary>
        public bool IsPresent => _warning != null;

        public bool IsAvailable => _warning != null && _warning.IsAvailable;

        public string AvailabilityNote => _warning == null
            ? "The early warning is not available on this machine."
            : _warning.IsAvailable
                ? "After each unit passes the station MES is asked GET_SMART_FA_ALTER_MSG. A warning never stops the machine."
                : "MES is not configured on this machine (MesUrl in App.config), so no early-warning check is made.";

        /// <summary>
        /// The spec's tick box: ticked = disabled. Changing it asks for the card first and
        /// stays as it was when the card is refused or the window is cancelled.
        /// </summary>
        public bool IsDisabled
        {
            get { return _warning != null && !_warning.IsPopupEnabled; }
            set
            {
                if (_warning == null || value == !_warning.IsPopupEnabled)
                    return;

                string refused = _warning.SetPopupEnabled(!value);
                if (refused != null)
                    _dialog?.ShowWarning(refused, Title);
                else if (_log != null && _session != null)
                    _log.Action(_session.UserName, (int)_session.Level, (value ? "disabled" : "enabled") + " the early-warning pop-up and buzzer");

                OnPropertyChanged();
                OnPropertyChanged(nameof(StateText));
            }
        }

        public string StateText => _warning == null ? string.Empty
            : _warning.IsPopupEnabled
                ? "Enabled: a warning from MES opens the pop-up, the red light and the buzzer."
                : "Disabled until the software restarts: no pop-up and no buzzer. MES is still asked after every unit.";

        public string CardNote => _warning != null && _warning.NeedsCard
            ? "Changing this needs the CM DRI's ID card, authorized by MES."
            : string.Empty;

        [ObservableProperty]
        private string _terminalName = string.Empty;

        [ObservableProperty]
        private string _serialNumber = string.Empty;

        [ObservableProperty]
        private string _requestBody = string.Empty;

        [ObservableProperty]
        private string _responseBody = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(TestCommand))]
        private bool _isTesting;

        public IAsyncRelayCommand TestCommand { get; }

        public IRelayCommand ResetCommand { get; }

        public override void OnShown()
        {
            OnPropertyChanged(nameof(IsDisabled));
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(AvailabilityNote));
            TestCommand.NotifyCanExecuteChanged();
        }

        private async Task TestAsync()
        {
            if (_warning == null)
                return;

            IsTesting = true;
            RequestBody = string.Empty;
            ResponseBody = "Asking MES...";
            try
            {
                EarlyWarningTest test = await _warning.TestAsync(TerminalName, SerialNumber);
                RequestBody = test.Request ?? string.Empty;
                ResponseBody = test.Response ?? string.Empty;
            }
            finally
            {
                IsTesting = false;
            }
        }

        /// <summary>"Clears the strings in the Terminal Name and Serial Number controls."</summary>
        private void Reset()
        {
            TerminalName = string.Empty;
            SerialNumber = string.Empty;
            RequestBody = string.Empty;
            ResponseBody = string.Empty;
        }
    }
}
