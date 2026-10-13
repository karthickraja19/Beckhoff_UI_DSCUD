using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// The IO service. The IO points are Data\Config.csv's; their
    /// state is read from the PLC through the Beckhoff library's card, whose ReadAllInput and
    /// ReadAllOutput return every input and every output as a bool[] indexed by the point's Id
    /// (the PLC's GVL_IO.aInput and GVL_IO.aOutput), and an output is set with the card's WriteDO.
    /// </summary>
    /// <remarks>
    /// The IO is read every <see cref="PollMs"/> only while something follows it
    /// (<see cref="FollowStatus"/>) - the I/O Diagnostics page, while it is on screen - and
    /// <see cref="IOChanged"/> is raised when a value changes. With no follower nothing is
    /// read and every point reads off, so a page opened later does not show old values.
    ///
    /// The cylinder and vacuum boards refresh on <see cref="IOChanged"/> too: it is also
    /// raised when the PLC pushes one of their HMI displays through
    /// <see cref="PlcSubscriptions"/>, and when the library has read one's current status on
    /// a board opening it (<see cref="ILibraryStatusSource"/>).
    /// </remarks>
    public sealed class TwincatDeviceIOService : IDigitalIOService, ILiveStatus
    {
        /// <summary>How often the IO is read while it is followed.</summary>
        private const int PollMs = 200;

        private readonly TwincatPlcConnection _plc;
        private readonly PlcSubscriptions _subscriptions;
        private readonly HashSet<string> _deviceSymbols;
        private readonly List<ILibraryStatusSource> _libraryDevices;
        private readonly List<IOPointSnapshot> _inputPoints;
        private readonly List<IOPointSnapshot> _outputPoints;
        private readonly Dictionary<string, int> _inputBits;
        private readonly Dictionary<string, int> _outputBits;

        /// <summary>Guards the followers, the timer and storing what was read.</summary>
        private readonly object _sync = new object();
        private int _followers;
        private Timer _timer;
        private int _reading;                      // 1 while a read is running
        private bool _reportedShortInputs;
        private bool _reportedShortOutputs;
        private volatile bool[] _inputs = new bool[0];
        private volatile bool[] _outputs = new bool[0];
        private bool _started;

        public event EventHandler IOChanged;

        /// <param name="inputs">The input points of Data\Config.csv; only their definitions are used.</param>
        /// <param name="outputs">The output points of Data\Config.csv; only their definitions are used.</param>
        /// <param name="devices">The cylinders and vacuums whose PLC pushes refresh the boards.</param>
        public TwincatDeviceIOService(
            IEnumerable<IOPointSnapshot> inputs,
            IEnumerable<IOPointSnapshot> outputs,
            TwincatPlcConnection plc,
            PlcSubscriptions subscriptions,
            IEnumerable<ITwincatStatusSource> devices)
        {
            if (inputs == null)
                throw new ArgumentNullException(nameof(inputs));
            if (outputs == null)
                throw new ArgumentNullException(nameof(outputs));

            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));

            var deviceList = (devices ?? Enumerable.Empty<ITwincatStatusSource>()).ToList();
            _deviceSymbols = new HashSet<string>(deviceList.SelectMany(d => d.WatchedSymbols), StringComparer.OrdinalIgnoreCase);
            _libraryDevices = deviceList.OfType<ILibraryStatusSource>().ToList();

            _inputPoints = inputs.OrderBy(p => p.Bit).ToList();
            _outputPoints = outputs.OrderBy(p => p.Bit).ToList();
            _inputBits = ByName(_inputPoints);
            _outputBits = ByName(_outputPoints);
        }

        public IReadOnlyList<IOPointSnapshot> ReadInputs() => Snapshot(_inputPoints, _inputs);

        public IReadOnlyList<IOPointSnapshot> ReadOutputs() => Snapshot(_outputPoints, _outputs);

        public bool ReadInput(string name) => IsOn(_inputBits, name, _inputs);

        public bool ReadOutput(string name) => IsOn(_outputBits, name, _outputs);

        public Task<bool> WriteOutputAsync(string name, bool on)
        {
            int bit;
            return _outputBits.TryGetValue(name ?? "", out bit)
                ? WriteOutputAsync(bit, on)
                : Task.FromResult(false);
        }

        /// <summary>Sets the output through the library's WriteDO; the next read shows what the PLC made of it.</summary>
        public Task<bool> WriteOutputAsync(int bit, bool on)
        {
            return Task.Run(() =>
            {
                bool written;
                try
                {
                    lock (_plc.SyncRoot)
                    {
                        written = _plc.IsConnected && _plc.Card.WriteDO(bit, on);
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "IO", "PLC", "Could not write output " + bit + ": " + ex.Message, ex.ToString());
                    written = false;
                }

                // Show the result straight away rather than at the next read.
                if (_followers > 0)
                    ThreadPool.QueueUserWorkItem(_ => Read());

                return written;
            });
        }

        public bool[] GetAllInputs()
        {
            int size = _inputPoints.Count == 0 ? 0 : _inputPoints.Max(p => p.Bit) + 1;
            bool[] inputs = _inputs;
            var all = new bool[size];
            Array.Copy(inputs, all, Math.Min(size, inputs.Length));
            return all;
        }

        /// <summary>
        /// Reads the IO from the PLC every <see cref="PollMs"/> until the returned handle is
        /// disposed. Each call counts: the reading stops when the last handle is disposed.
        /// </summary>
        public IDisposable FollowStatus()
        {
            lock (_sync)
            {
                if (_followers++ == 0)
                    _timer = new Timer(_ => Read(), null, 0, PollMs);
            }

            return new Following(this);
        }

        public void Start()
        {
            if (_started)
                return;

            _started = true;
            _subscriptions.ValueChanged += OnValueChanged;
            _subscriptions.LiveChanged += OnLiveChanged;
            foreach (ILibraryStatusSource device in _libraryDevices)
                device.LibraryStatusRead += OnLibraryStatusRead;
        }

        public void Stop()
        {
            if (!_started)
                return;

            _started = false;
            _subscriptions.ValueChanged -= OnValueChanged;
            _subscriptions.LiveChanged -= OnLiveChanged;
            foreach (ILibraryStatusSource device in _libraryDevices)
                device.LibraryStatusRead -= OnLibraryStatusRead;
        }

        private void Release()
        {
            lock (_sync)
            {
                if (--_followers > 0)
                    return;

                _timer.Dispose();
                _timer = null;
                _inputs = new bool[0];
                _outputs = new bool[0];
            }

            Raise();
        }

        /// <summary>
        /// One read of every input and output. Runs on a worker thread; a read still running
        /// when the next is due is not overlapped. With the PLC not there, everything reads off.
        /// </summary>
        private void Read()
        {
            if (Interlocked.Exchange(ref _reading, 1) == 1)
                return;

            try
            {
                bool[] inputs = null;
                bool[] outputs = null;

                lock (_plc.SyncRoot)
                {
                    if (_plc.IsConnected)
                    {
                        inputs = ReadArray(_plc.Card.ReadAllInput);
                        outputs = ReadArray(_plc.Card.ReadAllOutput);
                    }
                }

                inputs = inputs ?? new bool[0];
                outputs = outputs ?? new bool[0];
                CheckLength(inputs, _inputPoints, "inputs", ref _reportedShortInputs);
                CheckLength(outputs, _outputPoints, "outputs", ref _reportedShortOutputs);

                bool changed;
                lock (_sync)
                {
                    // Let go of while reading: keep nothing, so the next visit starts clean.
                    if (_followers == 0)
                        return;

                    changed = !inputs.SequenceEqual(_inputs) || !outputs.SequenceEqual(_outputs);
                    _inputs = inputs;
                    _outputs = outputs;
                }

                if (changed)
                    Raise();
            }
            finally
            {
                Interlocked.Exchange(ref _reading, 0);
            }
        }

        /// <summary>One of the card's IO reads, or null when it fails.</summary>
        private static bool[] ReadArray(Func<bool[]> read)
        {
            try
            {
                // A copy: the card also keeps what it read in its public Input and Output
                // arrays, and anything filling those again must not change the values held
                // here without IOChanged, or make the next read look like no change.
                bool[] values = read();
                return values == null ? null : (bool[])values.Clone();
            }
            catch (Exception ex)
            {
                // The library logs its own read failures; this is anything it let through.
                System.Diagnostics.Debug.WriteLine("IO read failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>Logs once when the library returns fewer points than Data\Config.csv lists; the rest read off.</summary>
        private static void CheckLength(bool[] values, List<IOPointSnapshot> points, string kind, ref bool reported)
        {
            if (reported || values.Length == 0 || points.Count == 0)
                return;

            int needed = points.Max(p => p.Bit) + 1;
            if (values.Length >= needed)
                return;

            reported = true;
            Logger.WriteLog(LogType.Error, "IO", "PLC", "The library returned " + values.Length + " " + kind
                + " but Data\\Config.csv has " + kind + " up to Id " + (needed - 1)
                + ", so the rest read off. Check InputCount/OutputCount in Data\\CardsConfiguration.json.", "");
        }

        private void OnValueChanged(object sender, string symbol)
        {
            if (_deviceSymbols.Contains(symbol))
                Raise();
        }

        private void OnLiveChanged(object sender, EventArgs e) => Raise();

        /// <summary>A board opened a cylinder or vacuum and the library has read its current status.</summary>
        private void OnLibraryStatusRead(object sender, EventArgs e) => Raise();

        private void Raise()
        {
            try
            {
                IOChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // Raised on a worker or the ADS thread: a faulty screen must not take it down.
                System.Diagnostics.Debug.WriteLine("An IO handler failed: " + ex.Message);
            }
        }

        private static IReadOnlyList<IOPointSnapshot> Snapshot(List<IOPointSnapshot> points, bool[] values)
        {
            return points
                .Select(p => new IOPointSnapshot { Bit = p.Bit, Name = p.Name, Address = p.Address, IsOn = IsOn(values, p.Bit) })
                .ToList();
        }

        private static bool IsOn(Dictionary<string, int> bits, string name, bool[] values)
        {
            int bit;
            return bits.TryGetValue(name ?? "", out bit) && IsOn(values, bit);
        }

        private static bool IsOn(bool[] values, int bit) => bit >= 0 && bit < values.Length && values[bit];

        private static Dictionary<string, int> ByName(IEnumerable<IOPointSnapshot> points)
        {
            return points
                .GroupBy(p => p.Name ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Bit, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>One <see cref="FollowStatus"/> call; disposing it lets go, once.</summary>
        private sealed class Following : IDisposable
        {
            private TwincatDeviceIOService _owner;

            public Following(TwincatDeviceIOService owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _owner, null)?.Release();
            }
        }
    }
}
