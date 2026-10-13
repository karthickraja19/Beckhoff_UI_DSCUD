using System;
using Nova.Abstractions.Services;
using Nova.Alarm;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// <see cref="IHomingService"/> over two PLC BOOLs: the command the button sets TRUE, and
    /// the done bit the PLC raises when homing has finished. The done bit is an ADS
    /// notification watched for the life of the HMI; the command is a plain write. The PLC
    /// owns both bits' lifecycle - it clears the command and the done bit itself.
    /// </summary>
    public sealed class TwincatHomingService : IHomingService
    {
        private readonly TwincatPlcConnection _plc;
        private readonly PlcSubscriptions _subscriptions;
        private readonly string _commandSymbol;
        private readonly string _doneSymbol;
        private readonly object _sync = new object();

        private bool _started;
        private bool _hasDone;
        private bool _done;
        private bool _homing;

        public event EventHandler StateChanged;
        public event EventHandler HomingCompleted;

        public TwincatHomingService(
            TwincatPlcConnection plc, PlcSubscriptions subscriptions, string commandSymbol, string doneSymbol)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _commandSymbol = commandSymbol ?? string.Empty;
            _doneSymbol = doneSymbol ?? string.Empty;
        }

        public bool IsAvailable => _commandSymbol.Length > 0;

        public string UnavailableReason => IsAvailable
            ? string.Empty
            : "PlcHomingCommandSymbol is not set in App.config.";

        public bool ReportsHomed => _doneSymbol.Length > 0;

        /// <summary>The value the PLC last pushed, read from the shared subscription so it is false while the PLC is not live.</summary>
        public bool IsHomed => ReportsHomed && _subscriptions.Get(_doneSymbol, false);

        public bool IsHoming
        {
            get { lock (_sync) return _homing; }
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_started)
                    return;

                _started = true;
            }

            if (_doneSymbol.Length == 0)
                return;

            _subscriptions.ValueChanged += OnValueChanged;
            _subscriptions.Watch(_doneSymbol, typeof(bool));
            Report();
        }

        public void StartHoming()
        {
            if (!IsAvailable)
                throw new InvalidOperationException(UnavailableReason);

            _plc.WriteSymbol(_commandSymbol, true);
            SetHoming(true);
        }

        /// <summary>Runs on the ADS thread: no ADS calls, and nothing slow.</summary>
        private void OnValueChanged(object sender, string symbol)
        {
            if (string.Equals(symbol, _doneSymbol, StringComparison.OrdinalIgnoreCase))
                Report();
        }

        private void Report()
        {
            bool done;
            if (!_subscriptions.TryGet(_doneSymbol, out done))
                return;

            bool completed;
            bool changed;
            lock (_sync)
            {
                // The value the PLC pushes on subscribing is the state it is already in, not a
                // homing that has just finished, so only a change after it counts.
                completed = _hasDone && done && !_done;
                changed = !_hasDone || done != _done;
                _hasDone = true;
                _done = done;
            }

            // IsHomed follows the done bit, both ways: the PLC clears it again (an E-stop,
            // a reset), and what is enabled only once homed has to know.
            if (changed)
                StateChanged?.Invoke(this, EventArgs.Empty);

            if (!done)
                return;

            SetHoming(false);
            if (completed)
                HomingCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void SetHoming(bool homing)
        {
            lock (_sync)
            {
                if (_homing == homing)
                    return;

                _homing = homing;
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
