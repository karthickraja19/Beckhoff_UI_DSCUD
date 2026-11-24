using System;
using System.Collections.Generic;
using System.Linq;
using Nova.Alarm;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat.Mapping
{
    /// <summary>
    /// Looks every variable of <see cref="PlcSymbolMap"/> up in the PLC and reports the ones
    /// that are not there, so a renamed or not yet implemented PLC variable is seen at
    /// startup rather than when its button is pressed.
    /// </summary>
    /// <remarks>
    /// Runs once the PLC is live: at startup when it already is, otherwise the first time
    /// it comes up. Each missing variable is written to the Error log with what it is for;
    /// the startup warnings carry a short summary. Nothing is changed in the map - a
    /// function whose variable is missing fails its own write or read, and says so.
    /// </remarks>
    public sealed class PlcSymbolCheck
    {
        private readonly TwincatPlcConnection _plc;
        private readonly PlcSubscriptions _subscriptions;
        private readonly PlcSymbolMap _map;
        private int _done;

        public PlcSymbolCheck(TwincatPlcConnection plc, PlcSubscriptions subscriptions, PlcSymbolMap map)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _map = map ?? throw new ArgumentNullException(nameof(map));
        }

        /// <summary>The mapped variables the PLC does not have, as "purpose = symbol". Empty until checked.</summary>
        public IReadOnlyList<string> Missing { get; private set; } = new string[0];

        /// <summary>True once the PLC has been asked.</summary>
        public bool HasRun => _done != 0;

        /// <summary>Raised on a worker thread when the check has run.</summary>
        public event EventHandler Completed;

        /// <summary>Checks now if the PLC is live, otherwise when it first is.</summary>
        public void Start()
        {
            _subscriptions.LiveChanged += (sender, e) => RunWhenLive();
            RunWhenLive();
        }

        private void RunWhenLive()
        {
            if (!_subscriptions.IsLive || System.Threading.Interlocked.Exchange(ref _done, 1) != 0)
                return;

            System.Threading.ThreadPool.QueueUserWorkItem(_ => Run());
        }

        private void Run()
        {
            var missing = new List<string>();
            try
            {
                foreach (var entry in _map.NamedSymbols())
                {
                    // The symbol's declaration, not its value: a struct or an array need not be read.
                    if (_plc.SymbolSize(entry.Value) == null)
                        missing.Add(entry.Key + " = " + entry.Value);
                }

                // A PLC that went away mid-check would make every later variable look missing.
                if (!_subscriptions.IsLive)
                    throw new InvalidOperationException("the PLC stopped answering during the check");
            }
            catch (Exception ex)
            {
                // The PLC went away mid-check: look again the next time it is live.
                System.Threading.Interlocked.Exchange(ref _done, 0);
                Logger.WriteLog(LogType.Error, "PLC", "SymbolCheck",
                    "The PLC symbol map could not be checked: " + ex.Message, ex.ToString());
                return;
            }

            Missing = missing;

            if (missing.Count == 0)
            {
                Logger.WriteLog(LogType.Info, "PLC", "SymbolCheck",
                    "Every variable in Data\\" + PlcSymbolMap.FileName + " exists in the PLC at " + _plc.Address, "");
            }
            else
            {
                Logger.WriteLog(LogType.Error, "PLC", "SymbolCheck",
                    missing.Count + " variable(s) in Data\\" + PlcSymbolMap.FileName + " are not in the PLC at " + _plc.Address
                    + ": " + string.Join("; ", missing), "");
            }

            Completed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>One line for the startup warnings, or null when nothing is missing.</summary>
        public string Summary()
        {
            if (Missing.Count == 0)
                return null;

            return Missing.Count + " PLC variable(s) named in Data\\" + PlcSymbolMap.FileName + " do not exist in the PLC, so "
                + "what they drive will not work: " + string.Join("; ", Missing.Take(8))
                + (Missing.Count > 8 ? "; ... (the Error log lists them all)" : "");
        }
    }
}
