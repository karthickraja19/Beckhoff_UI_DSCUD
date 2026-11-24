using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nova.Abstractions.Machine.Enums;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Nova.Wpf.App.Services.Twincat.Mapping;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// <see cref="IMachineModeLink"/> on the PLC: the run mode (FB_RunModeMapping), the bypass
    /// selections and the maintenance stations, all named in the symbol map (sections
    /// RunMode and Bypass).
    /// </summary>
    /// <remarks>
    /// The PLC decides. The run mode is a request the PLC takes over only while it allows a
    /// change (standing still and empty), so a write is followed by a wait for the PLC's own
    /// mode to become the one asked for; a bypass or a maintenance station is a level the
    /// HMI holds, read back after it is written. Everything reported comes from ADS
    /// notifications watched for the life of the HMI - a handful of small values.
    /// </remarks>
    public sealed class TwincatMachineModeLink : IMachineModeLink
    {
        /// <summary>How long the PLC gets to take over a requested mode.</summary>
        private static readonly TimeSpan TakeOverTimeout = TimeSpan.FromSeconds(2);

        private readonly TwincatPlcConnection _plc;
        private readonly PlcSubscriptions _subscriptions;
        private readonly string _requestSymbol;
        private readonly string _activeSymbol;
        private readonly string _changeAllowedSymbol;
        private readonly Dictionary<OperationMode, int> _modeValues = new Dictionary<OperationMode, int>();
        private readonly Dictionary<DeviceBypassKind, string[]> _bypassSymbols = new Dictionary<DeviceBypassKind, string[]>();
        private readonly Dictionary<int, string> _stationSymbols = new Dictionary<int, string>();
        private readonly HashSet<string> _watched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler Changed;

        public TwincatMachineModeLink(
            TwincatPlcConnection plc, PlcSubscriptions subscriptions, RunModeSymbols runMode, BypassSymbols bypass,
            ICollection<string> warnings)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            runMode = runMode ?? new RunModeSymbols();
            bypass = bypass ?? new BypassSymbols();

            _requestSymbol = PlcSymbolMap.Of(runMode.Request);
            _activeSymbol = PlcSymbolMap.Of(runMode.Active);
            _changeAllowedSymbol = PlcSymbolMap.Of(runMode.ChangeAllowed);

            foreach (var value in runMode.Values ?? new Dictionary<string, int?>())
            {
                OperationMode mode;
                if (value.Value == null)
                    continue;

                if (Enum.TryParse(value.Key, true, out mode))
                    _modeValues[mode] = value.Value.Value;
                else
                    warnings?.Add("RunMode.Values in " + PlcSymbolMap.FileName + " names \"" + value.Key
                        + "\", which is not an operation mode of this HMI.");
            }

            foreach (var device in bypass.Devices ?? new Dictionary<string, List<string>>())
            {
                DeviceBypassKind kind;
                string[] symbols = (device.Value ?? new List<string>()).Select(PlcSymbolMap.Of).Where(s => s.Length > 0).ToArray();
                if (symbols.Length == 0)
                    continue;

                if (Enum.TryParse(device.Key, true, out kind))
                    _bypassSymbols[kind] = symbols;
                else
                    warnings?.Add("Bypass.Devices in " + PlcSymbolMap.FileName + " names \"" + device.Key
                        + "\", which is not a bypass switch of this HMI.");
            }

            foreach (var station in bypass.MaintenanceStations ?? new Dictionary<string, string>())
            {
                int number;
                string symbol = PlcSymbolMap.Of(station.Value);
                if (symbol.Length > 0 && int.TryParse(station.Key, out number) && number > 0)
                    _stationSymbols[number] = symbol;
            }
        }

        /// <summary>Begins following what the PLC reports, for the life of the HMI.</summary>
        public void Start()
        {
            Watch(_activeSymbol, typeof(short));
            Watch(_changeAllowedSymbol, typeof(bool));
            foreach (string symbol in _bypassSymbols.Values.SelectMany(s => s))
                Watch(symbol, typeof(bool));
            foreach (string symbol in _stationSymbols.Values)
                Watch(symbol, typeof(bool));

            _subscriptions.ValueChanged += (sender, symbol) =>
            {
                if (_watched.Contains(symbol))
                    RaiseChanged();
            };
            _subscriptions.LiveChanged += (sender, e) => RaiseChanged();
        }

        private void Watch(string symbol, Type type)
        {
            if (symbol.Length > 0 && _watched.Add(symbol))
                _subscriptions.Watch(symbol, type);
        }

        public bool CanSelect(OperationMode mode)
        {
            return WhyNotSelectable(mode).Length == 0;
        }

        public string WhyNotSelectable(OperationMode mode)
        {
            // Maintenance is not a run mode of the PLC: it holds stations out of production.
            if (mode == OperationMode.Maintenance)
            {
                return _stationSymbols.Count > 0
                    ? string.Empty
                    : "The PLC has no maintenance station to hold out (Bypass.MaintenanceStations in " + PlcSymbolMap.FileName + ").";
            }

            if (_requestSymbol.Length == 0)
                return "The run mode is not wired to the PLC (RunMode.Request in " + PlcSymbolMap.FileName + ").";

            return _modeValues.ContainsKey(mode)
                ? string.Empty
                : "The PLC has no run mode for this (RunMode.Values in " + PlcSymbolMap.FileName + ").";
        }

        public bool CanSet(DeviceBypassKind kind)
        {
            return _bypassSymbols.ContainsKey(kind);
        }

        public bool CanHoldOut(int station)
        {
            return _stationSymbols.ContainsKey(station);
        }

        public MachineModeState Read()
        {
            if (!_subscriptions.IsLive)
            {
                return new MachineModeState
                {
                    IsLive = false,
                    NotLiveReason = _subscriptions.NotLiveReason ?? "PLC not connected",
                };
            }

            var state = new MachineModeState { IsLive = true };

            short active;
            if (_activeSymbol.Length > 0 && _subscriptions.TryGet(_activeSymbol, out active))
            {
                state.PlcModeNumber = active;
                foreach (var value in _modeValues)
                {
                    if (value.Value == active)
                    {
                        state.Mode = value.Key;
                        break;
                    }
                }
            }

            bool allowed;
            if (_changeAllowedSymbol.Length > 0 && _subscriptions.TryGet(_changeAllowedSymbol, out allowed))
                state.ChangeAllowed = allowed;

            foreach (var bypass in _bypassSymbols)
            {
                // A switch with several BOOLs is on only when all of them are.
                bool on = true;
                bool known = true;
                foreach (string symbol in bypass.Value)
                {
                    bool value;
                    if (!_subscriptions.TryGet(symbol, out value))
                        known = false;
                    else
                        on &= value;
                }

                if (known)
                    state.Bypasses[bypass.Key] = on;
            }

            var held = new List<int>();
            foreach (var station in _stationSymbols)
            {
                bool value;
                if (_subscriptions.TryGet(station.Value, out value) && value)
                    held.Add(station.Key);
            }

            held.Sort();
            state.MaintenanceStations = held;

            // Stations held out, and no other statement from the PLC: the HMI's Maintenance mode.
            if (held.Count > 0)
                state.Mode = OperationMode.Maintenance;

            return state;
        }

        public Task<MachineModeResult> ApplyAsync(MachineModeRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            return Task.Run(() => Apply(request));
        }

        private MachineModeResult Apply(MachineModeRequest request)
        {
            var result = new MachineModeResult();

            if (!_subscriptions.IsLive || !_plc.IsConnected)
            {
                result.Problems.Add("Nothing was changed: the PLC at " + _plc.Address + " is not reachable or not in Run.");
                return result;
            }

            // 1. Run mode. Maintenance leaves the PLC's run mode as it is.
            if (request.Mode != OperationMode.Maintenance)
            {
                string why = WhyNotSelectable(request.Mode);
                if (why.Length > 0)
                    result.Problems.Add("Mode not changed. " + why);
                else
                    ApplyMode(_modeValues[request.Mode], result);
            }

            // 2. Maintenance stations: the ones asked for held out, every other one put back.
            var wanted = new HashSet<int>(request.Mode == OperationMode.Maintenance
                ? (request.MaintenanceStations ?? new int[0])
                : new int[0]);
            foreach (int station in wanted.Where(s => !_stationSymbols.ContainsKey(s)))
                result.Problems.Add("Station " + station + " cannot be held out: the PLC has no variable for it.");
            foreach (var station in _stationSymbols)
                WriteLevel(station.Value, wanted.Contains(station.Key), "Maintenance station " + station.Key, result);

            // 3. Bypasses.
            foreach (var bypass in request.Bypasses ?? new Dictionary<DeviceBypassKind, bool>())
            {
                string[] symbols;
                if (!_bypassSymbols.TryGetValue(bypass.Key, out symbols))
                {
                    // Asked to switch on something the PLC cannot do is a problem; off is what it already is.
                    if (bypass.Value)
                        result.Problems.Add(bypass.Key + " bypass not set: the PLC has no bypass for it.");
                    continue;
                }

                foreach (string symbol in symbols)
                    WriteLevel(symbol, bypass.Value, bypass.Key + " bypass", result);
            }

            RaiseChanged();
            return result;
        }

        /// <summary>Writes the requested mode and waits for the PLC to take it over.</summary>
        private void ApplyMode(int value, MachineModeResult result)
        {
            try
            {
                short active;
                bool alreadyActive = _activeSymbol.Length > 0 && _subscriptions.TryGet(_activeSymbol, out active) && active == value;

                bool allowed;
                if (!alreadyActive && _changeAllowedSymbol.Length > 0
                    && _subscriptions.TryGet(_changeAllowedSymbol, out allowed) && !allowed)
                {
                    result.Problems.Add("Mode not changed: the PLC takes a new mode only while the machine is idle or in manual, "
                        + "and empty. Stop the machine, clear the units, then save again.");
                    return;
                }

                _plc.WriteSymbol(_requestSymbol, (short)value);

                if (_activeSymbol.Length == 0)
                    return;   // the PLC does not report its mode: the write is all that can be said

                DateTime deadline = DateTime.UtcNow + TakeOverTimeout;
                while (DateTime.UtcNow < deadline)
                {
                    if (Convert.ToInt32(_plc.ReadSymbol(_activeSymbol)) == value)
                        return;

                    Thread.Sleep(50);
                }

                result.Problems.Add("The mode was sent, but the PLC is still in mode " + Convert.ToInt32(_plc.ReadSymbol(_activeSymbol))
                    + ". It takes a new mode only while the machine is idle or in manual, and empty.");
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "PLC", "RunMode", "The run mode could not be written: " + ex.Message, ex.ToString());
                result.Problems.Add("Mode not changed: " + ex.Message);
            }
        }

        /// <summary>Writes a BOOL the HMI holds, and reads it back.</summary>
        private void WriteLevel(string symbol, bool value, string what, MachineModeResult result)
        {
            try
            {
                _plc.WriteSymbol(symbol, value);
                if (Convert.ToBoolean(_plc.ReadSymbol(symbol)) != value)
                    result.Problems.Add(what + ": the PLC did not keep the value (" + symbol + ").");
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "PLC", "DeviceSettings", what + " could not be written to " + symbol + ": " + ex.Message, ex.ToString());
                result.Problems.Add(what + " not written: " + ex.Message);
            }
        }

        private void RaiseChanged()
        {
            try
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // Raised on the ADS or a worker thread: a faulty screen must not take it down.
                System.Diagnostics.Debug.WriteLine("A machine mode handler failed: " + ex.Message);
            }
        }
    }
}
