using System;
using System.Threading.Tasks;
using Nova.Hive;
using Nova.Wpf.App.Services.Twincat.Mapping;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Pulses an HMI-owned BOOL of the symbol map: TRUE, then FALSE after the pulse length.
    /// The PLC acts on the rising edge. Pulses of one signal follow each other, never overlap.
    /// </summary>
    public sealed class PlcPulse
    {
        private readonly TwincatPlcConnection _plc;
        private readonly string _symbol;
        private readonly int _milliseconds;
        private readonly string _what;
        private readonly object _gate = new object();
        private Task _last = Task.FromResult(0);

        /// <param name="what">What the pulse is, for the log.</param>
        public PlcPulse(TwincatPlcConnection plc, string mappedSymbol, int milliseconds, string what)
        {
            _plc = plc;
            _symbol = PlcSymbolMap.Of(mappedSymbol);
            _milliseconds = Math.Max(50, milliseconds);
            _what = what ?? _symbol;
        }

        /// <summary>False while the map leaves the signal blank: nothing is written.</summary>
        public bool IsWired
        {
            get { return _plc != null && _symbol.Length > 0; }
        }

        /// <param name="before">Runs just before the bit goes TRUE, e.g. to write the data that goes with it.</param>
        public void Fire(Action before = null)
        {
            if (!IsWired)
                return;

            lock (_gate)
            {
                _last = _last.ContinueWith(async _ =>
                {
                    try
                    {
                        if (before != null)
                            before();

                        _plc.WriteSymbol(_symbol, true);
                        await Task.Delay(_milliseconds).ConfigureAwait(false);
                        _plc.WriteSymbol(_symbol, false);
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLog(LogType.Error, "PLC", "Signal",
                            "[Error/Alert] " + _what + " (" + _symbol + ") could not be written to the PLC: " + ex.Message, ex.ToString());
                    }
                }, TaskScheduler.Default).Unwrap();
            }
        }
    }

    /// <summary>
    /// S12 Data Upload Process Flow: a unit HIVE failed, or did not confirm after its tries,
    /// must not go to the next station. The HMI tells the PLC (symbol map:
    /// Process.UnitUploadNg, with the unit's serial number in Process.UnitUploadNgSerial) and
    /// the PLC diverts it to the NG conveyor.
    /// </summary>
    /// <remarks>
    /// The upload runs behind the unit and "does not gate the unit" (DFM slide 14): whether
    /// the PLC holds the unit until the answer is there is the PLC's decision. With the signal
    /// blank in the map nothing is written and the startup warnings say so.
    /// </remarks>
    public sealed class UnitUploadNgSignal
    {
        private readonly TwincatPlcConnection _plc;
        private readonly HiveService _hive;
        private readonly PlcPulse _pulse;
        private readonly string _serialSymbol;

        public UnitUploadNgSignal(TwincatPlcConnection plc, HiveService hive, ProcessSymbols symbols, int pulseMilliseconds)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _hive = hive ?? throw new ArgumentNullException(nameof(hive));
            symbols = symbols ?? new ProcessSymbols();
            _pulse = new PlcPulse(plc, symbols.UnitUploadNg, pulseMilliseconds, "The upload-NG signal");
            _serialSymbol = PlcSymbolMap.Of(symbols.UnitUploadNgSerial);
        }

        public bool IsWired
        {
            get { return _pulse.IsWired; }
        }

        public void Start()
        {
            if (IsWired)
                _hive.UnitDataResult += OnUnitDataResult;
        }

        private void OnUnitDataResult(object sender, HiveUnitResult result)
        {
            if (result == null
                || (result.Outcome != HiveUnitOutcome.Refused && result.Outcome != HiveUnitOutcome.NotConfirmed))
                return;

            string serial = result.Unit != null ? result.Unit.UnitSn ?? string.Empty : string.Empty;
            Logger.WriteLog(LogType.Error, "Hive", "UnitData",
                "[Error/Alert] Unit " + serial + " was " + (result.Outcome == HiveUnitOutcome.Refused ? "failed" : "not confirmed")
                + " by HIVE: the PLC is told to send it to the NG conveyor.", string.Empty);

            _pulse.Fire(() =>
            {
                if (_serialSymbol.Length > 0)
                    _plc.WriteSymbol(_serialSymbol, serial);
            });
        }
    }
}
