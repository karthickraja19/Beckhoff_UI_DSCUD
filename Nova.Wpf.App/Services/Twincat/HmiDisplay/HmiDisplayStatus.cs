using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Nova.Alarm;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// One device's live status. When a screen starts showing the device
    /// (<see cref="Follow"/>), the library reads its current status once - the device's
    /// Update...Properties, into the library object's properties - so the screen has it
    /// straight away; from then on the PLC pushes every change of the device's HMI display
    /// struct (the symbol the library object gives, <see cref="HmiDisplays.SymbolOf"/>, read
    /// as the struct App.config names for its kind) through <see cref="PlcSubscriptions"/>.
    /// Reading either never waits on the PLC.
    /// </summary>
    internal sealed class HmiDisplayStatus
    {
        private readonly PlcSubscriptions _subscriptions;
        private readonly TwincatPlcConnection _plc;
        private readonly Action _readFromLibrary;
        private readonly KeyValuePair<string, Type>[] _symbols;
        private int _followers;
        private volatile bool _libraryRead;

        /// <param name="libraryDevice">The library object that says where the struct is.</param>
        /// <param name="type">The struct App.config names; null when it names none that fits.</param>
        /// <param name="readFromLibrary">
        /// The library object's Update...Properties, which reads the device's current status
        /// from the PLC into its properties. Null for a device that has none.
        /// </param>
        public HmiDisplayStatus(
            PlcSubscriptions subscriptions, TwincatPlcConnection plc, object libraryDevice, Type type, Action readFromLibrary)
        {
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _readFromLibrary = readFromLibrary;
            Symbol = HmiDisplays.SymbolOf(libraryDevice);

            _symbols = Symbol == null || type == null
                ? new KeyValuePair<string, Type>[0]
                : new[] { new KeyValuePair<string, Type>(Symbol, type) };
            WatchedSymbols = _symbols.Select(s => s.Key).ToList();
        }

        /// <summary>
        /// The library has read the device's current status after <see cref="Follow"/>.
        /// Raised on a worker thread.
        /// </summary>
        public event EventHandler LibraryRead;

        /// <summary>The PLC path of the struct; null when the library gives none.</summary>
        public string Symbol { get; }

        /// <summary>The symbol, once there is also a struct to read it with; otherwise empty.</summary>
        public IReadOnlyList<string> WatchedSymbols { get; }

        /// <summary>
        /// True while the device is followed, the PLC is up and the library has read its
        /// status since - so the library object's properties can be shown until the PLC
        /// pushes the struct.
        /// </summary>
        public bool HasLibraryStatus => _libraryRead && _subscriptions.IsLive;

        /// <summary>
        /// Reads the current status through the library, then subscribes the struct, until the
        /// handle is disposed - while a screen shows the device. Quick on the UI thread: the
        /// read is done on a worker thread and announced by <see cref="LibraryRead"/>.
        /// </summary>
        public IDisposable Follow()
        {
            IDisposable subscription = _subscriptions.Follow(_symbols);
            Interlocked.Increment(ref _followers);

            if (_readFromLibrary != null)
                ThreadPool.QueueUserWorkItem(_ => ReadFromLibrary());

            return new Following(this, subscription);
        }

        /// <summary>Subscribes the struct for the life of the HMI - status needed whatever is on screen.</summary>
        public void Watch()
        {
            foreach (KeyValuePair<string, Type> symbol in _symbols)
                _subscriptions.Watch(symbol.Key, symbol.Value);
        }

        /// <summary>
        /// The struct the PLC last pushed, as <typeparamref name="TDisplay"/>. False, with
        /// <paramref name="reason"/> saying why for the screen to show, when there is none.
        /// </summary>
        public bool TryRead<TDisplay>(out TDisplay display, out string reason)
        {
            display = default(TDisplay);

            if (Symbol == null)
                reason = "No HMI display from the library - see the startup warnings";
            else if (_symbols.Length == 0)
                reason = "No HMI display type in App.config - see the startup warnings";
            else if (!_subscriptions.IsLive)
                reason = _subscriptions.NotLiveReason;
            else if (_subscriptions.IsMissing(Symbol))
                reason = "HMI display not usable - see the log";
            else if (!_subscriptions.IsFollowed(Symbol))
                reason = "Not monitored";
            else if (!_subscriptions.TryGet(Symbol, out display))
                reason = "Waiting for PLC";
            else
                reason = null;

            return reason == null;
        }

        private void ReadFromLibrary()
        {
            bool read = false;
            try
            {
                lock (_plc.SyncRoot)
                {
                    if (_plc.IsConnected)
                    {
                        _readFromLibrary();
                        read = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "PLC", "Status", "The library could not read a device's status: " + ex.Message, ex.ToString());
            }

            // Let go of while reading: nothing to show it on.
            if (Volatile.Read(ref _followers) == 0)
                return;

            _libraryRead = read;

            try
            {
                LibraryRead?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // A faulty screen must not take the worker thread down.
                Logger.WriteLog(LogType.Error, "PLC", "Status", "A status handler failed: " + ex.Message, ex.ToString());
            }
        }

        private void Release()
        {
            if (Interlocked.Decrement(ref _followers) == 0)
                _libraryRead = false;
        }

        /// <summary>One <see cref="Follow"/> call; disposing it lets go, once.</summary>
        private sealed class Following : IDisposable
        {
            private HmiDisplayStatus _owner;
            private readonly IDisposable _subscription;

            public Following(HmiDisplayStatus owner, IDisposable subscription)
            {
                _owner = owner;
                _subscription = subscription;
            }

            public void Dispose()
            {
                HmiDisplayStatus owner = Interlocked.Exchange(ref _owner, null);
                if (owner == null)
                    return;

                owner.Release();
                _subscription.Dispose();
            }
        }
    }
}
