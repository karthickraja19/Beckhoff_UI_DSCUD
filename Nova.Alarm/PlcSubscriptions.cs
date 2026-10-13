using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Teal.Library.Log;
using TwinCAT.Ads;

namespace Nova.Alarm
{
    /// <summary>
    /// Every live status the HMI shows from the PLC, as ADS notifications on the shared
    /// connection. A reader says which PLC symbols it needs - for the life of the HMI with
    /// <see cref="Watch"/>, or only while a screen shows them with <see cref="Follow"/> -
    /// and this subscribes them, keeps the last value the PLC pushed, and says when one
    /// changes. Nothing polls the PLC for status: a reader takes the values held here, so it
    /// is safe on any thread and never waits on the PLC.
    /// Declared here, in the lowest project that talks to the PLC, so HIVE can use it too.
    /// </summary>
    /// <remarks>
    /// A symbol is subscribed while anything follows it: the first follower subscribes it,
    /// the last one to let go drops it and its value. Only that one notification is added or
    /// deleted - the others, and their values, carry on.
    ///
    /// Same lifecycle as Nova.Alarm's PlcAlarmReader: subscribed on a worker thread, and
    /// subscribed again - every 5 seconds until it works - when the PLC leaves Run, drops the
    /// notifications (a new PLC program, an online change) or the connection goes. While
    /// there is no subscription <see cref="IsLive"/> is false and no values are held, so the
    /// screens say the PLC is not there instead of showing the last values as current.
    ///
    /// A symbol the PLC does not declare is logged once and left out; the others still run.
    /// A symbol followed by several readers (a sensor two devices read) is subscribed once.
    /// </remarks>
    public sealed class PlcSubscriptions : IDisposable
    {
        /// <summary>Compared every 100 ms, sent only on a change - and once straight after subscribing.</summary>
        private static readonly NotificationSettings OnChange = new NotificationSettings(AdsTransMode.OnChange, 100, 0);

        /// <summary>How long to wait before subscribing again when the PLC is not there.</summary>
        private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);

        private readonly IPlcConnection _plc;

        /// <summary>One subscribe or change of the subscription at a time. Held while calling the PLC.</summary>
        private readonly object _sync = new object();

        /// <summary>Guards <see cref="_wanted"/>. Never held while calling the PLC, so the UI thread can take it.</summary>
        private readonly object _wantedLock = new object();

        /// <summary>The symbols something follows, with how many followers each has.</summary>
        private readonly Dictionary<string, Wanted> _wanted = new Dictionary<string, Wanted>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Symbols already logged as missing, so a retry does not log them again.</summary>
        private readonly HashSet<string> _reportedMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private volatile Generation _current;     // null = not subscribed
        private volatile HashSet<string> _missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private volatile string _notLiveReason = "Waiting for PLC";
        private volatile bool _started;
        private Timer _retry;                     // only runs while not subscribed
        private int _subscribeQueued;             // 1 while a Subscribe is waiting to run
        private int _syncQueued;                  // 1 while a Sync is waiting to run
        private bool _failing;

        public PlcSubscriptions(IPlcConnection plc)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
        }

        /// <summary>
        /// A value the PLC pushed, with the symbol it belongs to. Raised on the ADS thread:
        /// handlers must be quick, must not take locks the ADS thread could wait on, and must
        /// not call the PLC.
        /// </summary>
        public event EventHandler<string> ValueChanged;

        /// <summary><see cref="IsLive"/> changed. Raised on the ADS or a worker thread.</summary>
        public event EventHandler LiveChanged;

        /// <summary>True while subscribed with the PLC in Run - the values held are current.</summary>
        public bool IsLive
        {
            get { return _notLiveReason == null; }
        }

        /// <summary>Why nothing is live - "PLC not connected", "PLC not in Run" - or null while live.</summary>
        public string NotLiveReason
        {
            get { return _notLiveReason; }
        }

        public string Address
        {
            get { return _plc.Address; }
        }

        /// <summary>
        /// Follows a symbol for as long as the HMI runs - status that is needed whatever is on
        /// screen, such as the HIVE machine state. <paramref name="type"/> is how the PLC value
        /// is read: bool for BOOL, double for LREAL, uint for UDINT, or a struct laid out byte
        /// for byte like the PLC's. An empty symbol is ignored, so callers can pass optional ones.
        /// </summary>
        public void Watch(string symbol, Type type)
        {
            Add(new[] { new KeyValuePair<string, Type>(symbol, type) }, OnChange);
        }

        /// <summary>
        /// <see cref="Watch(string, Type)"/>, with the PLC checking the value for a change every
        /// <paramref name="checkEvery"/> instead of every 100 ms - for a value the PLC can change
        /// several times in 100 ms, where each change matters (a sequence's log). Only a change
        /// that lasts at least that long is seen. A symbol something already follows keeps the
        /// check it was first subscribed with.
        /// </summary>
        public void Watch(string symbol, Type type, TimeSpan checkEvery)
        {
            Add(new[] { new KeyValuePair<string, Type>(symbol, type) },
                new NotificationSettings(AdsTransMode.OnChange, (int)checkEvery.TotalMilliseconds, 0));
        }

        /// <summary>
        /// Follows the symbols until the returned handle is disposed - status only needed
        /// while a screen shows it. Each call counts: a symbol stays subscribed while any
        /// handle following it is open. Quick on the UI thread: the subscribing is done on a
        /// worker thread, and the values arrive once it is. Empty symbols are ignored.
        /// </summary>
        public IDisposable Follow(IEnumerable<KeyValuePair<string, Type>> symbols)
        {
            List<KeyValuePair<string, Type>> followed = (symbols ?? Enumerable.Empty<KeyValuePair<string, Type>>())
                .Where(s => !string.IsNullOrWhiteSpace(s.Key))
                .ToList();

            Add(followed, OnChange);
            return new Following(this, followed);
        }

        /// <summary>True while something follows the symbol.</summary>
        public bool IsFollowed(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return false;

            lock (_wantedLock)
            {
                return _wanted.ContainsKey(symbol);
            }
        }

        /// <summary>The last value the PLC pushed for the symbol. False while not live, while no one follows it, before the first value, or when the symbol could not be subscribed.</summary>
        public bool TryGet<T>(string symbol, out T value)
        {
            Generation current = _current;
            SymbolTag tag;
            if (!string.IsNullOrWhiteSpace(symbol) && IsLive && current != null
                && current.Symbols.TryGetValue(symbol, out tag) && tag.Value is T typed)
            {
                value = typed;
                return true;
            }

            value = default(T);
            return false;
        }

        /// <summary>The value, or <paramref name="fallback"/> when there is none.</summary>
        public T Get<T>(string symbol, T fallback = default(T))
        {
            T value;
            return TryGet(symbol, out value) ? value : fallback;
        }

        /// <summary>True when the symbol could not be subscribed - the PLC does not declare it, or its size is wrong.</summary>
        public bool IsMissing(string symbol)
        {
            return !string.IsNullOrWhiteSpace(symbol) && _missing.Contains(symbol);
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_started)
                    return;

                _started = true;
                AdsClient client = _plc.Client;
                client.AdsNotificationEx += OnAdsNotification;
                client.AdsNotificationsInvalidated += OnNotificationsInvalidated;
            }

            // Not on the caller's thread: reaching a PLC that is not running can take seconds.
            QueueSubscribe();
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (!_started)
                    return;

                _started = false;
                StopRetry();
                Unsubscribe("Stopped");

                AdsClient client = _plc.Client;
                client.AdsNotificationEx -= OnAdsNotification;
                client.AdsNotificationsInvalidated -= OnNotificationsInvalidated;
            }
        }

        /// <summary>The connection belongs to whoever created it, so it is left open.</summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>Counts a follower for each symbol, and subscribes the ones nothing followed before.</summary>
        private void Add(IEnumerable<KeyValuePair<string, Type>> symbols, NotificationSettings settings)
        {
            bool added = false;
            lock (_wantedLock)
            {
                foreach (KeyValuePair<string, Type> symbol in symbols)
                {
                    if (string.IsNullOrWhiteSpace(symbol.Key))
                        continue;

                    Wanted wanted;
                    if (!_wanted.TryGetValue(symbol.Key, out wanted))
                    {
                        _wanted[symbol.Key] = wanted = new Wanted(symbol.Value, settings);
                        added = true;
                    }

                    wanted.Count++;
                }
            }

            // Before Start, the first subscribe takes in whatever is wanted by then.
            if (added && _started)
                QueueSync();
        }

        /// <summary>Takes a follower off each symbol, and drops the ones nothing follows any more.</summary>
        private void Release(IEnumerable<KeyValuePair<string, Type>> symbols)
        {
            bool dropped = false;
            lock (_wantedLock)
            {
                foreach (KeyValuePair<string, Type> symbol in symbols)
                {
                    Wanted wanted;
                    if (_wanted.TryGetValue(symbol.Key, out wanted) && --wanted.Count == 0)
                    {
                        _wanted.Remove(symbol.Key);
                        dropped = true;
                    }
                }
            }

            if (dropped && _started)
                QueueSync();
        }

        private List<KeyValuePair<string, Wanted>> WantedNow()
        {
            lock (_wantedLock)
            {
                return _wanted.ToList();
            }
        }

        /// <summary>Subscribes on a worker thread. Several requests close together run once.</summary>
        private void QueueSubscribe()
        {
            if (Interlocked.Exchange(ref _subscribeQueued, 1) == 0)
                ThreadPool.QueueUserWorkItem(_ => Subscribe());
        }

        /// <summary>Brings the subscription in line with what is followed, on a worker thread. Several requests close together run once.</summary>
        private void QueueSync()
        {
            if (Interlocked.Exchange(ref _syncQueued, 1) == 0)
                ThreadPool.QueueUserWorkItem(_ => Sync());
        }

        /// <summary>
        /// Subscribes every followed symbol and the PLC state, dropping the old subscription
        /// first. If the PLC is not there or not in Run, tries again in 5 seconds.
        /// </summary>
        private void Subscribe()
        {
            Interlocked.Exchange(ref _subscribeQueued, 0);

            lock (_sync)
            {
                if (!_started)
                    return;

                StopRetry();
                Unsubscribe("PLC not connected");

                try
                {
                    // Reconnects if the connection dropped; true only with the PLC in Run.
                    if (!_plc.IsConnected)
                        throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");

                    var generation = new Generation();
                    List<KeyValuePair<string, Wanted>> wanted = WantedNow();
                    var missing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // symbol -> why

                    lock (_plc.SyncRoot)
                    {
                        AdsClient client = _plc.Client;

                        // Set before subscribing: the first notification can arrive before
                        // AddDeviceNotificationEx returns.
                        _current = generation;

                        generation.StateHandle = client.AddDeviceNotificationEx(
                            (uint)AdsReservedIndexGroup.DeviceData, (uint)AdsReservedIndexOffset.DeviceDataAdsState,
                            OnChange, generation.StateTag, typeof(ushort));

                        foreach (KeyValuePair<string, Wanted> symbol in wanted)
                        {
                            string problem = SubscribeSymbol(client, generation, symbol.Key, symbol.Value);
                            if (problem != null)
                                missing[symbol.Key] = problem;
                        }
                    }

                    // Nothing subscribed usually means the connection went while subscribing,
                    // not that every symbol is wrong. Check before believing the symbols.
                    if (wanted.Count > 0 && missing.Count == wanted.Count && !PlcIsRunning())
                        throw new InvalidOperationException("the PLC went away while subscribing");

                    _missing = new HashSet<string>(missing.Keys, StringComparer.OrdinalIgnoreCase);
                    ReportMissing(missing);
                    SetLive(null);

                    if (_failing)
                        Logger.WriteLog(LogType.Info, "PLC", "Subscriptions", "Subscribed to the PLC status again", "");

                    _failing = false;
                }
                catch (Exception ex)
                {
                    Unsubscribe("PLC not connected or not in Run");

                    // Logged once, not every 5 seconds.
                    if (!_failing)
                    {
                        _failing = true;
                        LogError("Cannot subscribe to the PLC status - trying again every " + RetryAfter.TotalSeconds + " s", ex);
                    }

                    _retry = new Timer(_ => Subscribe(), null, RetryAfter, Timeout.InfiniteTimeSpan);
                }
            }
        }

        /// <summary>
        /// Subscribes the symbols followed since the last subscribe or sync, and drops the
        /// ones nothing follows any more. Everything else is left as it is. With no
        /// subscription there is nothing to change: the next subscribe takes in whatever is
        /// followed by then.
        /// </summary>
        private void Sync()
        {
            Interlocked.Exchange(ref _syncQueued, 0);

            lock (_sync)
            {
                Generation current = _current;
                if (!_started || current == null)
                    return;

                var wanted = WantedNow().ToDictionary(w => w.Key, w => w.Value, StringComparer.OrdinalIgnoreCase);
                var missing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    lock (_plc.SyncRoot)
                    {
                        AdsClient client = _plc.Client;

                        foreach (string symbol in current.Symbols.Keys.Where(s => !wanted.ContainsKey(s)).ToList())
                        {
                            SymbolTag tag;
                            if (!current.Symbols.TryRemove(symbol, out tag))
                                continue;

                            // A notification already on its way for it is ignored from here.
                            tag.Active = false;
                            client.TryDeleteDeviceNotification(tag.Handle);
                        }

                        foreach (KeyValuePair<string, Wanted> symbol in wanted)
                        {
                            if (current.Symbols.ContainsKey(symbol.Key) || _missing.Contains(symbol.Key))
                                continue;

                            string problem = SubscribeSymbol(client, current, symbol.Key, symbol.Value);
                            if (problem != null)
                                missing[symbol.Key] = problem;
                        }
                    }
                }
                catch (Exception)
                {
                    // The connection went while changing the subscription. Subscribing again
                    // keeps trying until the PLC is back, and logs why.
                    QueueSubscribe();
                    return;
                }

                if (missing.Count == 0)
                    return;

                // A symbol that fails because the PLC went away is not missing.
                if (!PlcIsRunning())
                {
                    QueueSubscribe();
                    return;
                }

                var all = new HashSet<string>(_missing, StringComparer.OrdinalIgnoreCase);
                all.UnionWith(missing.Keys);
                _missing = all;
                ReportMissing(missing);
            }
        }

        /// <summary>Subscribes one symbol. Returns why it could not, or null once it is subscribed.</summary>
        private string SubscribeSymbol(AdsClient client, Generation generation, string symbol, Wanted wanted)
        {
            Type type = wanted.Type;
            try
            {
                // A struct of the wrong size would be read with every field after the first
                // difference shifted, and look plausible - so it is checked first.
                if (!type.IsPrimitive)
                {
                    int expected = Marshal.SizeOf(type);
                    int size = client.ReadSymbol(symbol).ByteSize;
                    if (size != expected)
                    {
                        return symbol + " is " + size + " bytes but " + type.Name + " is " + expected
                            + " (the PLC struct and " + type.Name + " must declare the same fields in the same order)";
                    }
                }

                var tag = new SymbolTag(generation, symbol);
                tag.Handle = client.AddDeviceNotificationEx(symbol, wanted.Settings, tag, type);
                generation.Symbols[symbol] = tag;
                return null;
            }
            catch (Exception ex)
            {
                return symbol + " (" + ex.Message + ")";
            }
        }

        private bool PlcIsRunning()
        {
            try
            {
                lock (_plc.SyncRoot)
                {
                    return _plc.Client.ReadState().AdsState == AdsState.Run;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Logs symbols that could not be subscribed, each once for the life of the HMI.</summary>
        private void ReportMissing(Dictionary<string, string> missing)
        {
            var fresh = new List<string>();
            foreach (KeyValuePair<string, string> symbol in missing)
            {
                if (_reportedMissing.Add(symbol.Key))
                    fresh.Add(symbol.Value);
            }

            if (fresh.Count > 0)
            {
                Logger.WriteLog(LogType.Error, "PLC", "Subscriptions",
                    fresh.Count + " PLC symbol(s) could not be subscribed and show no live value: "
                    + string.Join("; ", fresh.Take(20)) + (fresh.Count > 20 ? "; ..." : ""), "");
            }
        }

        /// <summary>Drops the current subscription and every value, and marks nothing live.</summary>
        private void Unsubscribe(string reason)
        {
            Generation old = _current;
            _current = null;
            SetLive(reason);

            if (old == null)
                return;

            foreach (SymbolTag tag in old.Symbols.Values)
                tag.Active = false;

            try
            {
                lock (_plc.SyncRoot)
                {
                    AdsClient client = _plc.Client;
                    if (!client.IsConnected)
                        return;

                    // After a new PLC program the handles are already gone, so a failure here is normal.
                    client.TryDeleteDeviceNotification(old.StateHandle);
                    foreach (SymbolTag tag in old.Symbols.Values)
                        client.TryDeleteDeviceNotification(tag.Handle);
                }
            }
            catch (Exception ex)
            {
                LogError("Could not drop the PLC status subscription", ex);
            }
        }

        private void SetLive(string notLiveReason)
        {
            string before = _notLiveReason;
            _notLiveReason = notLiveReason;

            if ((before == null) != (notLiveReason == null))
            {
                try
                {
                    LiveChanged?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    LogError("A PLC status handler failed", ex);
                }
            }
        }

        private void StopRetry()
        {
            if (_retry != null)
            {
                _retry.Dispose();
                _retry = null;
            }
        }

        /// <summary>
        /// A notification arrived. This runs on the ADS thread and the connection is shared:
        /// no locks and no ADS calls in here, or the connection can deadlock - anything that
        /// needs the PLC is queued.
        /// </summary>
        private void OnAdsNotification(object sender, AdsNotificationExEventArgs e)
        {
            Generation current = _current;
            if (current == null)
                return;

            try
            {
                if (e.UserData == current.StateTag)
                {
                    // No data means the PLC dropped the notification, and leaving Run throws the
                    // subscriptions away. Either way subscribe again; that keeps trying until the
                    // PLC is back in Run.
                    if (!(e.Value is ushort) || (AdsState)(ushort)e.Value != AdsState.Run)
                    {
                        SetLive("PLC not in Run");
                        QueueSubscribe();
                    }

                    return;
                }

                // Anything else is someone else's on the shared connection, from a
                // subscription that has since been replaced, or for a symbol just dropped.
                var tag = e.UserData as SymbolTag;
                if (tag == null || tag.Generation != current || !tag.Active)
                    return;

                if (e.Value == null)
                {
                    QueueSubscribe();   // no data: the PLC dropped the notification
                    return;
                }

                tag.Value = e.Value;
                ValueChanged?.Invoke(this, tag.Symbol);
            }
            catch (Exception ex)
            {
                // A faulty screen must not take the ADS thread down.
                LogError("A PLC status handler failed", ex);
            }
        }

        /// <summary>The PLC dropped some notifications - usually a new PLC program. Subscribe again.</summary>
        private void OnNotificationsInvalidated(object sender, AdsNotificationsInvalidatedEventArgs e)
        {
            QueueSubscribe();
        }

        private static void LogError(string message, Exception ex)
        {
            Logger.WriteLog(LogType.Error, "PLC", "Subscriptions", message + ": " + ex.Message, ex.ToString());
        }

        /// <summary>A followed symbol: how its value is read, how often the PLC checks it, and how many followers it has.</summary>
        private sealed class Wanted
        {
            public Wanted(Type type, NotificationSettings settings)
            {
                Type = type;
                Settings = settings;
            }

            public Type Type { get; }

            public NotificationSettings Settings { get; }

            public int Count;
        }

        /// <summary>One subscription: the PLC-state notification, and the notification of each symbol subscribed.</summary>
        private sealed class Generation
        {
            public readonly object StateTag = new object();
            public uint StateHandle;

            /// <summary>Changed only while holding the subscription lock; read from any thread.</summary>
            public readonly ConcurrentDictionary<string, SymbolTag> Symbols =
                new ConcurrentDictionary<string, SymbolTag>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// User data of one symbol's notification: which symbol, which subscription it belongs
        /// to, and the last value the PLC pushed for it.
        /// </summary>
        private sealed class SymbolTag
        {
            public SymbolTag(Generation generation, string symbol)
            {
                Generation = generation;
                Symbol = symbol;
            }

            public Generation Generation { get; }

            public string Symbol { get; }

            public uint Handle;

            /// <summary>False once the symbol is dropped; a notification already on its way is then ignored.</summary>
            public volatile bool Active = true;

            /// <summary>Null until the PLC sends the first value.</summary>
            public volatile object Value;
        }

        /// <summary>One <see cref="Follow"/> call; disposing it lets go of its symbols, once.</summary>
        private sealed class Following : IDisposable
        {
            private readonly List<KeyValuePair<string, Type>> _symbols;
            private PlcSubscriptions _owner;

            public Following(PlcSubscriptions owner, List<KeyValuePair<string, Type>> symbols)
            {
                _owner = owner;
                _symbols = symbols;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _owner, null)?.Release(_symbols);
            }
        }
    }
}
