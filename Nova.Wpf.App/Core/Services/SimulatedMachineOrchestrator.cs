using System;
using System.Threading;
using System.Threading.Tasks;
using Nova.Abstractions.Machine.Enums;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Stand-in for the PLC's machine state machine, so Start / Pause / Stop / Homing /
    /// Reset can be exercised with no PLC. The TwinCAT orchestrator replaces it by writing
    /// the machine command bits and reading the state back; the gates here (no start with
    /// an alarm standing or before homing) are the PLC's to enforce there.
    /// </summary>
    public sealed class SimulatedMachineOrchestrator : IMachineOrchestrator, IDisposable
    {
        private const int SimulatedHomingMs = 2000;

        private readonly IMachineContext _context;
        private readonly IAlarmService _alarms;
        private readonly IDialogService _dialogs;
        private readonly SynchronizationContext _uiContext;
        private CancellationTokenSource _homingCts;

        public event Action<MachineStatus> OnMachineStatusChanged;

        public SimulatedMachineOrchestrator(IMachineContext context, IAlarmService alarms, IDialogService dialogs)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _alarms = alarms ?? throw new ArgumentNullException(nameof(alarms));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

            // Resolved on the UI thread at shell construction. Homing runs off it, so status
            // events and dialogs are marshalled back through this context.
            _uiContext = SynchronizationContext.Current;

            _alarms.OnAlarmTriggered += OnAlarmTriggered;
        }

        public MachineStatus CurrentStatus => _context.CurrentStatus;

        public bool IsHomingDone => _context.IsHomingDone;

        public bool IsAutoRunning =>
            _context.CurrentStatus == MachineStatus.Running || _context.CurrentStatus == MachineStatus.Pause;

        /// <summary>Start, or resume when paused.</summary>
        public Task StartAutoAsync()
        {
            var status = _context.CurrentStatus;
            if (status == MachineStatus.Pause)
            {
                Resume();
                return Task.CompletedTask;
            }

            if (status == MachineStatus.Running || status == MachineStatus.Homing)
                return Task.CompletedTask;

            if (_alarms.HasActiveAlarm)
            {
                Warn("Reset the active alarms before starting.", "Start");
                return Task.CompletedTask;
            }

            if (!_context.IsHomingDone)
            {
                Warn("Run homing before starting.", "Start");
                return Task.CompletedTask;
            }

            SetStatus(MachineStatus.Running);
            return Task.CompletedTask;
        }

        public void Stop()
        {
            Interlocked.Exchange(ref _homingCts, null)?.Cancel();
            _context.RequestStop();
            RaiseStatusChanged();
        }

        public void Pause()
        {
            if (_context.CurrentStatus != MachineStatus.Running)
                return;

            _context.RequestPause();
            RaiseStatusChanged();
        }

        public void Resume()
        {
            if (_context.CurrentStatus != MachineStatus.Pause)
                return;

            _context.RequestResume();
            RaiseStatusChanged();
        }

        public async Task StartHomingAsync()
        {
            var status = _context.CurrentStatus;
            if (status == MachineStatus.Running || status == MachineStatus.Pause || status == MachineStatus.Homing)
                return;

            if (_alarms.HasActiveAlarm)
            {
                Warn("Reset the active alarms before homing.", "Homing");
                return;
            }

            var cts = new CancellationTokenSource();
            Interlocked.Exchange(ref _homingCts, cts)?.Cancel();

            _context.IsHomingDone = false;
            SetStatus(MachineStatus.Homing);

            try
            {
                await Task.Delay(SimulatedHomingMs, cts.Token).ConfigureAwait(false);
                _context.IsHomingDone = true;
                SetStatus(MachineStatus.Idle);
            }
            catch (OperationCanceledException)
            {
                // Stopped or alarmed while homing; that path has already set the status.
            }
            finally
            {
                Interlocked.CompareExchange(ref _homingCts, null, cts);
            }
        }

        public void ResetAlarms()
        {
            _alarms.ResetAllAlarms();
            _context.HasActiveAlarm = false;

            var status = _context.CurrentStatus;
            if (status == MachineStatus.Alarm || status == MachineStatus.Stop)
                SetStatus(MachineStatus.Idle);
        }

        public void Dispose()
        {
            _alarms.OnAlarmTriggered -= OnAlarmTriggered;
        }

        private void OnAlarmTriggered(object sender, AlarmEventArgs e)
        {
            Interlocked.Exchange(ref _homingCts, null)?.Cancel();
            _context.HasActiveAlarm = true;
            SetStatus(MachineStatus.Alarm);
        }

        private void SetStatus(MachineStatus status)
        {
            _context.CurrentStatus = status;
            RaiseStatusChanged();
        }

        private void RaiseStatusChanged()
        {
            var handler = OnMachineStatusChanged;
            if (handler == null)
                return;

            var status = _context.CurrentStatus;
            if (_uiContext != null)
                _uiContext.Post(_ => handler(status), null);
            else
                handler(status);
        }

        private void Warn(string message, string title)
        {
            if (_uiContext != null)
                _uiContext.Send(_ => _dialogs.ShowWarning(message, title), null);
            else
                _dialogs.ShowWarning(message, title);
        }
    }
}
