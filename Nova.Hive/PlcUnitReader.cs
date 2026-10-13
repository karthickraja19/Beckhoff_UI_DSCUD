using System;
using System.Threading.Tasks;
using Nova.Alarm;
using Teal.Library.Log;

namespace Nova.Hive
{
    /// <summary>
    /// Tells HIVE when a unit enters the machine (Audio SW DFM v5.4, slides 7 and 8: HIVE is
    /// Running while units are being processed and goes to Idle 90 s after the last one).
    /// The PLC reports it with one symbol, as an ADS notification through the HMI's shared
    /// <see cref="PlcSubscriptions"/> - nothing polls:
    /// <list type="bullet">
    /// <item>a BOOL the PLC sets for each unit that enters: every rising edge is one unit</item>
    /// <item>or a counter (INT) the PLC increments: every change is one unit</item>
    /// </list>
    /// </summary>
    public sealed class PlcUnitReader : IDisposable
    {
        private readonly PlcSubscriptions _subscriptions;
        private readonly string _symbol;
        private readonly bool _isCounter;
        private readonly object _sync = new object();

        /// <summary>Units are handed on in order, off the ADS thread: the HIVE side may write files.</summary>
        private Task _delivery = Task.FromResult(0);

        private bool _started;
        private bool _hasValue;
        private short _lastCount;
        private bool _lastBool;

        /// <summary>Raised on a worker thread, once for each unit that entered.</summary>
        public event EventHandler UnitEntered;

        /// <param name="isCounter">True when the symbol is an INT counter; false for a BOOL pulse.</param>
        public PlcUnitReader(PlcSubscriptions subscriptions, string symbol, bool isCounter)
        {
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _symbol = symbol;
            _isCounter = isCounter;
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_started)
                    return;

                _started = true;
            }

            _subscriptions.ValueChanged += OnValueChanged;
            _subscriptions.Watch(_symbol, _isCounter ? typeof(short) : typeof(bool));

            // Take the value the PLC pushed first as the starting point, so a counter that
            // is already at 1234 does not count as 1234 units, or a BOOL that is already
            // TRUE as one.
            Report(initial: true);
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (!_started)
                    return;

                _started = false;
            }

            _subscriptions.ValueChanged -= OnValueChanged;
        }

        /// <summary>The subscriptions are shared and belong to their creator, so they are left running.</summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>Runs on the ADS thread: no ADS calls, and nothing slow.</summary>
        private void OnValueChanged(object sender, string symbol)
        {
            if (string.Equals(symbol, _symbol, StringComparison.OrdinalIgnoreCase))
                Report(initial: false);
        }

        private void Report(bool initial)
        {
            bool entered;

            if (_isCounter)
            {
                short value;
                if (!_subscriptions.TryGet(_symbol, out value))
                    return;

                lock (_sync)
                {
                    if (!_started)
                        return;

                    entered = _hasValue && !initial && value != _lastCount;
                    _hasValue = true;
                    _lastCount = value;
                }
            }
            else
            {
                bool value;
                if (!_subscriptions.TryGet(_symbol, out value))
                    return;

                lock (_sync)
                {
                    if (!_started)
                        return;

                    entered = _hasValue && !initial && value && !_lastBool;
                    _hasValue = true;
                    _lastBool = value;
                }
            }

            if (!entered)
                return;

            lock (_sync)
            {
                _delivery = _delivery.ContinueWith(_ => Deliver(), TaskScheduler.Default);
            }
        }

        private void Deliver()
        {
            try
            {
                UnitEntered?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "Unit", "Handling a unit entering failed: " + ex.Message, ex.ToString());
            }
        }
    }
}
