using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Nova.Alarm;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Takes the rows the PLC's custom logs (<c>FB_CustomLog</c>) queue and writes each one
    /// through its <see cref="ICustomLog"/>. The PLC logs a whole struct; the log's model is the
    /// HMI's copy of that struct, so one row is one <c>customLog.Log(model)</c>.
    /// </summary>
    /// <remarks>
    /// Per log, the PLC block holds <c>nWriteCount</c> (rows logged), <c>nAckCount</c> (rows the
    /// HMI has taken) and a queue <c>aSlot</c>; row n is in slot n MOD slots. A change of
    /// <c>nWriteCount</c> arrives as an ADS notification; the bridge then reads the rows from
    /// <c>nAckCount</c> on, writes them, and writes <c>nAckCount</c> back. The HMI keeps no
    /// count of its own, so a restart of the HMI loses no row and repeats none, and rows
    /// logged while the PLC connection was down are taken when it is back.
    ///
    /// Rows overwritten before they were taken (the HMI was away for more than a queue) are
    /// counted by the PLC (<c>nLost</c>) and reported here. A row whose size does not match the
    /// model is dropped with a message: it would otherwise block the queue for good.
    /// </remarks>
    public sealed class PlcCustomLogBridge : IDisposable
    {
        /// <summary>The PLC checks the count for a change this often.</summary>
        private static readonly TimeSpan CheckEvery = TimeSpan.FromMilliseconds(20);

        /// <summary>Rows are looked for this often anyway, so a read that failed is repeated without waiting for the next row.</summary>
        private static readonly TimeSpan SweepEvery = TimeSpan.FromSeconds(5);

        private readonly TwincatPlcConnection _plc;
        private readonly PlcSubscriptions _subscriptions;
        private readonly List<Binding> _bindings = new List<Binding>();
        private readonly object _sync = new object();
        private readonly Action<string, object> _rowHandler;
        private Timer _sweep;
        private volatile bool _started;

        /// <param name="logs">The custom logs by key (<c>CustomLogs.All</c>).</param>
        /// <param name="symbols">The PLC block (the <c>FB_CustomLog</c> instance) of each log, by the same key. A log with none is not fed from the PLC.</param>
        /// <param name="rowHandler">Called with the log's key and the typed model of every row taken from the PLC, e.g. to save it in the database. It runs
        /// on a worker thread, before the CSV row is written, and may block; an exception is reported and does not stop the CSV row.</param>
        public PlcCustomLogBridge(TwincatPlcConnection plc, PlcSubscriptions subscriptions,
            IReadOnlyDictionary<string, ICustomLog> logs, IDictionary<string, string> symbols,
            Action<string, object> rowHandler = null)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _rowHandler = rowHandler;

            foreach (KeyValuePair<string, string> entry in symbols ?? new Dictionary<string, string>())
            {
                if (string.IsNullOrWhiteSpace(entry.Value))
                    continue;

                ICustomLog log;
                if (logs == null || !logs.TryGetValue(entry.Key, out log))
                {
                    Logger.WriteLog(LogType.Error, "CustomLog", entry.Key,
                        "PlcSymbols.json CustomLog.Logs names '" + entry.Key + "', but the HMI has no custom log with that key (CustomLogs.All).", "");
                    continue;
                }

                _bindings.Add(new Binding(entry.Key, entry.Value.Trim(), log));
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_started || _bindings.Count == 0)
                    return;

                _started = true;
            }

            _subscriptions.ValueChanged += OnValueChanged;
            _subscriptions.LiveChanged += OnLiveChanged;
            foreach (Binding binding in _bindings)
                _subscriptions.Watch(binding.CountSymbol, typeof(uint), CheckEvery);

            _sweep = new Timer(_ => ScheduleAll(), null, SweepEvery, SweepEvery);
            ScheduleAll();
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (!_started)
                    return;

                _started = false;
            }

            _sweep?.Dispose();
            _sweep = null;
            _subscriptions.ValueChanged -= OnValueChanged;
            _subscriptions.LiveChanged -= OnLiveChanged;
        }

        /// <summary>The subscriptions are shared and belong to their creator, so they are left running.</summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>Runs on the ADS thread: nothing slow, no ADS calls.</summary>
        private void OnValueChanged(object sender, string symbol)
        {
            foreach (Binding binding in _bindings)
            {
                if (string.Equals(binding.CountSymbol, symbol, StringComparison.OrdinalIgnoreCase))
                    Schedule(binding);
            }
        }

        private void OnLiveChanged(object sender, EventArgs e)
        {
            if (_subscriptions.IsLive)
                ScheduleAll();
        }

        private void ScheduleAll()
        {
            if (!_started || !_subscriptions.IsLive)
                return;

            foreach (Binding binding in _bindings)
                Schedule(binding);
        }

        private void Schedule(Binding binding)
        {
            if (Interlocked.Exchange(ref binding.Queued, 1) == 0)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    // Cleared first: a row logged while this runs asks for another pass.
                    Interlocked.Exchange(ref binding.Queued, 0);
                    Drain(binding);
                });
            }
        }

        /// <summary>Takes every row the PLC holds for the log and acknowledges them.</summary>
        private void Drain(Binding binding)
        {
            lock (binding.Sync)
            {
                uint ack = 0;
                uint taken = 0;
                try
                {
                    if (!_started)
                        return;

                    if (binding.Slots == 0 && !binding.ReadLayout(_plc))
                        return;

                    uint write = Convert.ToUInt32(_plc.ReadSymbol(binding.Symbol + ".nWriteCount"));
                    ack = Convert.ToUInt32(_plc.ReadSymbol(binding.Symbol + ".nAckCount"));

                    if (ack == write)
                        return;

                    // Counters are not persistent: the HMI's count can only be ahead after the PLC program was replaced.
                    if (unchecked(write - ack) > uint.MaxValue / 2)
                    {
                        _plc.WriteSymbol(binding.Symbol + ".nAckCount", write);
                        return;
                    }

                    uint pending = unchecked(write - ack);
                    if (pending > binding.Slots)
                    {
                        uint lost = pending - (uint)binding.Slots;
                        Report(binding, "lost", lost + " row(s) of the " + binding.Key + " log were overwritten in the PLC before the HMI took them.");
                        ack = unchecked(ack + lost);
                        taken = lost;
                    }

                    // Read the rows first, then check they were not overwritten while reading.
                    var rows = new List<KeyValuePair<uint, byte[]>>();
                    uint first = ack;
                    for (uint n = ack; n != write; n = unchecked(n + 1))
                    {
                        string slot = binding.Symbol + ".aSlot[" + (n % (uint)binding.Slots) + "]";
                        uint size = Convert.ToUInt32(_plc.ReadSymbol(slot + ".nSize"));
                        if (size == 0 || size > binding.MaxBytes)
                        {
                            rows.Add(new KeyValuePair<uint, byte[]>(n, null));
                            continue;
                        }

                        rows.Add(new KeyValuePair<uint, byte[]>(n, _plc.ReadBytes(slot + ".aData", (int)size)));
                    }

                    uint writeNow = Convert.ToUInt32(_plc.ReadSymbol(binding.Symbol + ".nWriteCount"));
                    foreach (KeyValuePair<uint, byte[]> row in rows)
                    {
                        if (unchecked(writeNow - row.Key) > (uint)binding.Slots)
                            Report(binding, "overwritten", "a row of the " + binding.Key + " log was overwritten in the PLC while the HMI was reading it.");
                        else
                            Write(binding, row.Value);

                        ack = unchecked(row.Key + 1);
                        taken++;
                    }
                }
                catch (Exception ex)
                {
                    while (ex.InnerException != null)
                        ex = ex.InnerException;

                    // Rows already written are acknowledged below; the rest is taken on the next pass.
                    Report(binding, "read", "could not read the " + binding.Key + " log from the PLC: " + ex.Message);
                }
                finally
                {
                    if (taken > 0)
                    {
                        try
                        {
                            _plc.WriteSymbol(binding.Symbol + ".nAckCount", ack);
                        }
                        catch (Exception ex)
                        {
                            Report(binding, "ack", "could not acknowledge the " + binding.Key + " log to the PLC: " + ex.Message);
                        }
                    }
                }
            }
        }

        /// <summary>One row: its bytes back into the model, then <c>customLog.Log(model)</c>.</summary>
        private void Write(Binding binding, byte[] data)
        {
            if (data == null)
            {
                Report(binding, "size", "a row of the " + binding.Key + " log was empty or bigger than the PLC queue holds, and was dropped.");
                return;
            }

            int expected = Marshal.SizeOf(binding.Log.ModelType);
            if (data.Length != expected)
            {
                Report(binding, "size", "the PLC's " + binding.Key + " struct is " + data.Length + " bytes but " + binding.Log.ModelType.Name
                    + " is " + expected + ": the members or their order differ. The rows are dropped until they match.");
                return;
            }

            GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                object model = Marshal.PtrToStructure(pin.AddrOfPinnedObject(), binding.Log.ModelType);

                // The handler (the database) does not depend on the CSV target being usable.
                if (_rowHandler != null)
                {
                    try
                    {
                        _rowHandler(binding.Key, model);
                    }
                    catch (Exception ex)
                    {
                        while (ex.InnerException != null)
                            ex = ex.InnerException;

                        Report(binding, "handler", "a row of the " + binding.Key + " log could not be saved: " + ex.Message);
                    }
                }

                if (!binding.Log.IsReady)
                    Report(binding, "notready", "the " + binding.Key + " log is not ready (see the earlier error about its NLog target), so its CSV rows are dropped.");
                else
                    binding.Log.LogModel(model);
            }
            catch (Exception ex)
            {
                Report(binding, "convert", "a row of the " + binding.Key + " log could not be converted: " + ex.Message);
            }
            finally
            {
                pin.Free();
            }
        }

        /// <summary>Says it once per kind of problem and log, so a stuck PLC cannot fill the log.</summary>
        private static void Report(Binding binding, string kind, string message)
        {
            string key = kind + message;
            if (binding.LastReported.TryGetValue(kind, out string last) && last == key)
                return;

            binding.LastReported[kind] = key;
            Logger.WriteLog(LogType.Error, "CustomLog", binding.Key, message, "");
        }

        private sealed class Binding
        {
            public readonly string Key;
            public readonly string Symbol;
            public readonly ICustomLog Log;
            public readonly object Sync = new object();
            public readonly Dictionary<string, string> LastReported = new Dictionary<string, string>();
            public int Queued;
            public int Slots;
            public uint MaxBytes;

            public Binding(string key, string symbol, ICustomLog log)
            {
                Key = key;
                Symbol = symbol;
                Log = log;
            }

            public string CountSymbol => Symbol + ".nWriteCount";

            /// <summary>How many rows the PLC's queue holds and the biggest row, from the declared sizes (no constants to read).</summary>
            public bool ReadLayout(TwincatPlcConnection plc)
            {
                int? all = plc.SymbolSize(Symbol + ".aSlot");
                int? one = plc.SymbolSize(Symbol + ".aSlot[0]");
                int? data = plc.SymbolSize(Symbol + ".aSlot[0].aData");
                if (all == null || one == null || data == null || one.Value == 0)
                    return false;

                Slots = all.Value / one.Value;
                MaxBytes = (uint)data.Value;
                return Slots > 0;
            }
        }
    }
}
