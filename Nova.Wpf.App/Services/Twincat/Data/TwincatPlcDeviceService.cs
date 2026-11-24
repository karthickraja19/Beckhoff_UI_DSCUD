using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Nova.Wpf.App.Services.Twincat.Mapping;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// <see cref="IPlcDeviceService"/> over one kind of PLC device block (symbol map:
    /// ThirdPartyDevices.Dispenser / Weighing / Vibration). Every key the Manual tab uses is
    /// a member of the block named in the map, with its PLC type and scale.
    /// </summary>
    /// <remarks>
    /// Values are ADS notifications, followed only while the device's tab is on screen.
    /// Commands are written one at a time, in order, on a worker thread. A command button is
    /// TRUE then FALSE after the pulse length - the PLC acts on it while TRUE, and clearing it
    /// is harmless where the PLC has already done so. Values are written and read back.
    /// </remarks>
    public sealed class TwincatPlcDeviceService : IPlcDeviceService
    {
        private readonly TwincatPlcConnection _plc;
        private readonly PlcSubscriptions _subscriptions;
        private readonly DeviceBlockSymbols _block;
        private readonly List<DeviceInstanceSymbol> _instances;
        private readonly string _kind;
        private readonly object _sync = new object();
        private Task _writes = Task.CompletedTask;

        public event EventHandler Changed;

        /// <param name="kind">What the devices are, for messages ("dispenser").</param>
        public TwincatPlcDeviceService(TwincatPlcConnection plc, PlcSubscriptions subscriptions, DeviceBlockSymbols block, string kind)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _block = block ?? new DeviceBlockSymbols();
            _kind = kind ?? "device";
            _instances = _block.MappedInstances.ToList();

            _subscriptions.ValueChanged += OnValueChanged;
            _subscriptions.LiveChanged += (sender, e) => Raise();
        }

        public IReadOnlyList<string> Devices => _instances
            .Select((i, n) => string.IsNullOrWhiteSpace(i.Name) ? _kind + " " + (n + 1) : i.Name.Trim())
            .ToList();

        public bool IsAvailable => _instances.Count > 0;

        public string UnavailableReason => IsAvailable
            ? string.Empty
            : "No " + _kind + " is mapped to the PLC (ThirdPartyDevices in Data\\" + PlcSymbolMap.FileName + ").";

        public bool IsLive => _subscriptions.IsLive;

        public string NotLiveReason => _subscriptions.IsLive ? string.Empty : _subscriptions.NotLiveReason ?? "PLC not connected";

        public bool Has(string key)
        {
            return _block.Member(key) != null;
        }

        private string PathOf(int device, string key)
        {
            DeviceMemberSymbol member = _block.Member(key);
            if (member == null || device < 0 || device >= _instances.Count)
                return string.Empty;

            return _instances[device].Path.Trim() + "." + member.Symbol.Trim();
        }

        public IDisposable Follow(int device)
        {
            if (device < 0 || device >= _instances.Count)
                return new Nothing();

            var symbols = new List<KeyValuePair<string, Type>>();
            foreach (var member in _block.Members ?? new Dictionary<string, DeviceMemberSymbol>())
            {
                if (member.Value == null || !PlcSymbolMap.Has(member.Value.Symbol))
                    continue;

                Type type = ClrType(member.Value.Type);
                if (type == null)
                {
                    Logger.WriteLog(LogType.Error, "PLC", _kind, member.Key + " has PLC type \"" + member.Value.Type
                        + "\", which the HMI cannot read; it is not shown.", string.Empty);
                    continue;
                }

                symbols.Add(new KeyValuePair<string, Type>(PathOf(device, member.Key), type));
            }

            IDisposable following = _subscriptions.Follow(symbols);
            Raise();
            return following;
        }

        public bool TryGetFlag(int device, string key, out bool value)
        {
            value = false;
            object raw;
            if (!TryGetRaw(device, key, out raw))
                return false;

            value = raw is bool ? (bool)raw : Convert.ToDouble(raw, CultureInfo.InvariantCulture) != 0;
            return true;
        }

        public bool TryGetNumber(int device, string key, out double value)
        {
            value = 0;
            object raw;
            if (!TryGetRaw(device, key, out raw) || raw is IPlcString)
                return false;

            double number = raw is bool ? ((bool)raw ? 1 : 0) : Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            double scale = _block.Member(key).Scale;
            value = scale != 0 ? number / scale : number;
            return true;
        }

        public bool TryGetText(int device, string key, out string value)
        {
            value = null;
            object raw;
            if (!TryGetRaw(device, key, out raw))
                return false;

            var text = raw as IPlcString;
            value = text != null ? (text.Value ?? string.Empty).Trim() : Convert.ToString(raw, CultureInfo.InvariantCulture);
            return true;
        }

        private bool TryGetRaw(int device, string key, out object raw)
        {
            raw = null;
            string path = PathOf(device, key);
            return path.Length > 0 && _subscriptions.TryGet(path, out raw) && raw != null;
        }

        public Task PulseAsync(int device, string key)
        {
            string path = Require(device, key);
            int pulse = _block.Member(key).PulseMilliseconds > 0 ? _block.Member(key).PulseMilliseconds
                : _block.PulseMilliseconds > 0 ? _block.PulseMilliseconds : 200;
            return QueueWrite(key, () =>
            {
                _plc.WriteSymbol(path, true);
                Thread.Sleep(pulse);
                _plc.WriteSymbol(path, false);
            });
        }

        public Task SetFlagAsync(int device, string key, bool on)
        {
            string path = Require(device, key);
            return QueueWrite(key, () => _plc.WriteSymbol(path, on));
        }

        public Task WriteAsync(int device, IReadOnlyDictionary<string, double> values)
        {
            var writes = new List<Tuple<string, string, DeviceMemberSymbol, double>>();
            foreach (var value in values ?? new Dictionary<string, double>())
                writes.Add(Tuple.Create(value.Key, Require(device, value.Key), _block.Member(value.Key), value.Value));

            return QueueWrite("settings", () =>
            {
                var problems = new List<string>();
                foreach (var write in writes)
                {
                    try
                    {
                        object plcValue = ToPlc(write.Item4 * (write.Item3.Scale != 0 ? write.Item3.Scale : 1), write.Item3.Type);
                        _plc.WriteSymbol(write.Item2, plcValue);
                        object back = _plc.ReadSymbol(write.Item2);
                        if (!SameValue(plcValue, back))
                            problems.Add(write.Item1 + ": the PLC kept " + Convert.ToString(back, CultureInfo.InvariantCulture));
                    }
                    catch (Exception ex)
                    {
                        problems.Add(write.Item1 + ": " + ex.Message);
                    }
                }

                if (problems.Count > 0)
                    throw new InvalidOperationException(string.Join("; ", problems));
            });
        }

        private string Require(int device, string key)
        {
            if (device < 0 || device >= _instances.Count)
                throw new InvalidOperationException("no " + _kind + " " + (device + 1) + " is mapped");

            string path = PathOf(device, key);
            if (path.Length == 0)
                throw new InvalidOperationException(key + " is not mapped (ThirdPartyDevices in Data\\" + PlcSymbolMap.FileName + ")");

            return path;
        }

        /// <summary>Runs after every write queued before it, on a worker thread; the task faults when it fails.</summary>
        private Task QueueWrite(string what, Action write)
        {
            lock (_sync)
            {
                Task queued = _writes.ContinueWith(_ =>
                {
                    if (!_plc.IsConnected)
                        throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");

                    try
                    {
                        write();
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLog(LogType.Error, "PLC", _kind, what + " could not be written: " + ex.Message, ex.ToString());
                        throw;
                    }
                }, TaskScheduler.Default);

                _writes = queued.ContinueWith(_ => { }, TaskScheduler.Default);
                return queued;
            }
        }

        private static bool SameValue(object written, object back)
        {
            if (back == null)
                return false;
            if (written is bool)
                return back is bool ? (bool)back == (bool)written : Convert.ToDouble(back, CultureInfo.InvariantCulture) != 0 == (bool)written;

            double a = Convert.ToDouble(written, CultureInfo.InvariantCulture);
            double b = Convert.ToDouble(back, CultureInfo.InvariantCulture);
            return Math.Abs(a - b) <= Math.Max(1e-6, Math.Abs(a) * 1e-6);
        }

        /// <summary>The value as the PLC type, for a write.</summary>
        private static object ToPlc(double value, string plcType)
        {
            switch ((plcType ?? "LREAL").Trim().ToUpperInvariant())
            {
                case "BOOL": return value != 0;
                case "SINT": return (sbyte)Math.Round(value);
                case "USINT": case "BYTE": return (byte)Math.Round(value);
                case "INT": return (short)Math.Round(value);
                case "UINT": case "WORD": return (ushort)Math.Round(value);
                case "DINT": return (int)Math.Round(value);
                case "UDINT": case "DWORD": case "TIME": return (uint)Math.Max(0, Math.Round(value));
                case "REAL": return (float)value;
                case "LREAL": return value;
                default: throw new InvalidOperationException("a " + plcType + " cannot be written as a number");
            }
        }

        /// <summary>The C# type a notification of the PLC type is read as; null for one the HMI cannot read.</summary>
        private static Type ClrType(string plcType)
        {
            string type = (plcType ?? string.Empty).Trim().ToUpperInvariant();
            switch (type)
            {
                case "BOOL": return typeof(bool);
                case "SINT": return typeof(sbyte);
                case "USINT": case "BYTE": return typeof(byte);
                case "INT": return typeof(short);
                case "UINT": case "WORD": return typeof(ushort);
                case "DINT": return typeof(int);
                case "UDINT": case "DWORD": case "TIME": return typeof(uint);
                case "REAL": return typeof(float);
                case "LREAL": return typeof(double);
                case "STRING": return typeof(PlcString80);
            }

            if (type.StartsWith("STRING(", StringComparison.Ordinal) && type.EndsWith(")", StringComparison.Ordinal))
            {
                int length;
                if (int.TryParse(type.Substring(7, type.Length - 8), out length))
                {
                    switch (length)
                    {
                        case 20: return typeof(PlcString20);
                        case 30: return typeof(PlcString30);
                        case 40: return typeof(PlcString40);
                        case 50: return typeof(PlcString50);
                        case 80: return typeof(PlcString80);
                        case 100: return typeof(PlcString100);
                        case 120: return typeof(PlcString120);
                        case 255: return typeof(PlcString255);
                    }
                }
            }

            return null;
        }

        /// <summary>Runs on the ADS thread: nothing slow.</summary>
        private void OnValueChanged(object sender, string symbol)
        {
            foreach (DeviceInstanceSymbol instance in _instances)
            {
                if (symbol.StartsWith(instance.Path.Trim() + ".", StringComparison.OrdinalIgnoreCase))
                {
                    Raise();
                    return;
                }
            }
        }

        private void Raise()
        {
            try
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("A device status handler failed: " + ex.Message);
            }
        }

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    /// <summary>The three kinds of third-party device, each on its own section of the map.</summary>
    public sealed class TwincatThirdPartyDevices : IThirdPartyDevices
    {
        public TwincatThirdPartyDevices(TwincatPlcConnection plc, PlcSubscriptions subscriptions, ThirdPartyDeviceSymbols map)
        {
            map = map ?? new ThirdPartyDeviceSymbols();
            Dispensers = new TwincatPlcDeviceService(plc, subscriptions, map.Dispenser, "dispenser");
            WeighingScales = new TwincatPlcDeviceService(plc, subscriptions, map.Weighing, "weighing scale");
            VibrationSensors = new TwincatPlcDeviceService(plc, subscriptions, map.Vibration, "vibration sensor");
        }

        public IPlcDeviceService Dispensers { get; }

        public IPlcDeviceService WeighingScales { get; }

        public IPlcDeviceService VibrationSensors { get; }
    }

    /// <summary>A PLC STRING(n) read through a notification: n characters and the closing zero byte.</summary>
    public interface IPlcString
    {
        string Value { get; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct PlcString20 : IPlcString { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)] public string Text; public string Value => Text; }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct PlcString30 : IPlcString { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)] public string Text; public string Value => Text; }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct PlcString40 : IPlcString { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 41)] public string Text; public string Value => Text; }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct PlcString50 : IPlcString { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 51)] public string Text; public string Value => Text; }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct PlcString80 : IPlcString { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)] public string Text; public string Value => Text; }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct PlcString100 : IPlcString { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 101)] public string Text; public string Value => Text; }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct PlcString120 : IPlcString { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 121)] public string Text; public string Value => Text; }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct PlcString255 : IPlcString { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Text; public string Value => Text; }
}
