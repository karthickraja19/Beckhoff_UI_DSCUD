using System;
using System.Threading;
using System.Threading.Tasks;
using Nova.Abstractions.Machine.Enums;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// The run state behind the shell's Start / Pause / Stop buttons, held in the HMI
    /// (<see cref="IMachineContext"/>). Nothing here commands the PLC: the PLC owns the
    /// machine sequence and its gates, and the orchestrator that writes its machine command
    /// bits and reads the state back replaces this one. Homing is the PLC's
    /// (<see cref="IHomingService"/>), and Start waits for its homing-done bit.
    /// </summary>
    public sealed class MachineOrchestrator : IMachineOrchestrator
    {
        private readonly IMachineContext _context;
        private readonly IDialogService _dialogs;
        private readonly IHomingService _homing;
        private readonly SynchronizationContext _uiContext;

        public event Action<MachineStatus> OnMachineStatusChanged;

        public MachineOrchestrator(IMachineContext context, IDialogService dialogs, IHomingService homing)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _homing = homing ?? throw new ArgumentNullException(nameof(homing));

            // Resolved on the UI thread at shell construction, so status events and dialogs
            // raised from another thread are marshalled back through this context.
            _uiContext = SynchronizationContext.Current;
        }

        public MachineStatus CurrentStatus => _context.CurrentStatus;

        /// <summary>The PLC's homing-done bit; false while it is not wired (PlcHomingDoneSymbol).</summary>
        public bool IsHomingDone => _homing.ReportsHomed && _homing.IsHomed;

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

            if (!IsHomingDone)
            {
                Warn(!_homing.ReportsHomed
                    ? "PlcHomingDoneSymbol is not set in App.config, so homing done cannot be read from the PLC."
                    : "Run homing before starting (the PLC has not reported homing done).", "Start");
                return Task.CompletedTask;
            }

            SetStatus(MachineStatus.Running);
            return Task.CompletedTask;
        }

        public void Stop()
        {
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

        /// <summary>Raises the PLC's home command bit, as the Home page's Homing button does.</summary>
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
        /// Clears the HMI's own alarm state. The alarms themselves are the PLC's, cleared by
        /// its RESET.
        /// </summary>
        public void ResetAlarms()
        {
            _context.HasActiveAlarm = false;

            var status = _context.CurrentStatus;
            if (status == MachineStatus.Alarm || status == MachineStatus.Stop)
                SetStatus(MachineStatus.Idle);
        }

        /// <summary>The HMI-held state has no manual mode; the PLC's is set by TwincatMachineOrchestrator.</summary>
        public void SetManualMode(bool on)
        {
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
