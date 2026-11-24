using System;
using System.Threading.Tasks;
using Nova.Alarm;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// An event the PLC reports by counting one variable up (a finished unit, a finished
    /// weighing). Each step up is handed on, in order, on a worker thread, where the handler
    /// may read the PLC: the PLC holds the event's data until it counts the next one.
    /// </summary>
    /// <remarks>
    /// The counter is an ADS notification watched for the life of the HMI. The value it has
    /// when first seen is the starting point, not that many events, and a counter that goes
    /// down (a PLC restart) is a new starting point. A DINT or a UDINT both read as 32 bits.
    /// </remarks>
    public sealed class PlcEventCounter
    {
        private readonly PlcSubscriptions _subscriptions;
        private readonly string _symbol;
        private readonly string _what;
        private readonly Action<int> _handler;
        private readonly object _sync = new object();

        private Task _delivery = Task.FromResult(0);
        private bool _started;
        private bool _hasCount;
        private long _lastCount;

        /// <param name="what">What is counted, for the log ("finished unit", "glue weighing").</param>
        /// <param name="handler">Called with how many events happened since the last call. Runs on a worker thread.</param>
        public PlcEventCounter(PlcSubscriptions subscriptions, string symbol, string what, Action<int> handler)
        {
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _symbol = (symbol ?? string.Empty).Trim();
            _what = what ?? "event";
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public bool IsWired => _symbol.Length > 0;

        public void Start()
        {
            lock (_sync)
            {
                if (_started || !IsWired)
                    return;

                _started = true;
            }

            _subscriptions.ValueChanged += (sender, symbol) =>
            {
                if (string.Equals(symbol, _symbol, StringComparison.OrdinalIgnoreCase))
                    Report();
            };
            _subscriptions.Watch(_symbol, typeof(uint));
            Report();
        }

        /// <summary>Runs on the ADS thread: nothing slow, the handler runs on a worker.</summary>
        private void Report()
        {
            uint raw;
            if (!_subscriptions.TryGet(_symbol, out raw))
                return;

            long count = raw;
            lock (_sync)
            {
                long steps = _hasCount && count > _lastCount ? count - _lastCount : 0;
                _hasCount = true;
                _lastCount = count;
                if (steps == 0)
                    return;

                int events = (int)Math.Min(steps, int.MaxValue);
                _delivery = _delivery.ContinueWith(_ => Deliver(events), TaskScheduler.Default);
            }
        }

        private void Deliver(int events)
        {
            try
            {
                _handler(events);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "PLC", "Events", "A " + _what + " could not be handled: " + ex.Message, ex.ToString());
            }
        }
    }
}
