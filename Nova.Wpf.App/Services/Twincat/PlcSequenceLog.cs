using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Nova.Alarm;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// The PLC sequences' logs, in the running log. Each sequence object in the PLC
    /// (LoadingConveyor, UnloadingConveyor, ...) writes its last log entry into its own
    /// <see cref="ST_Sequence"/>; App.config's PlcSequenceLogSymbols lists where they are.
    /// Each is an ADS notification through the shared <see cref="PlcSubscriptions"/>, watched
    /// for the life of the HMI, and every new entry goes through <see cref="Logger.WriteLog"/>:
    /// into the running-log panel, and into the SequenceLog files, one folder per station.
    /// </summary>
    /// <remarks>
    /// The PLC keeps only the last entry, so the HMI sees an entry only if it stays at least
    /// <see cref="CheckEvery"/> - a sequence that logs twice in one PLC cycle shows the second
    /// only - and the same entry written twice in a row shows once.
    ///
    /// When the HMI starts, each sequence's current entry is logged once, so the log starts
    /// with where every sequence is. After the PLC comes back, an entry is logged only if it
    /// changed while the PLC was away.
    /// </remarks>
    public sealed class PlcSequenceLog : IDisposable
    {
        /// <summary>How often the PLC checks each log for a new entry. Sequence steps can be much shorter than the usual 100 ms.</summary>
        private static readonly TimeSpan CheckEvery = TimeSpan.FromMilliseconds(10);

        private readonly PlcSubscriptions _subscriptions;
        private readonly List<string> _symbols;
        private readonly object _sync = new object();

        /// <summary>The last entry seen for each symbol. Kept while the PLC is away, so a reconnect does not log it again.</summary>
        private readonly Dictionary<string, ST_Sequence> _last = new Dictionary<string, ST_Sequence>(StringComparer.OrdinalIgnoreCase);

        /// <summary>New entries, written in order off the ADS thread: writing a log file is slow.</summary>
        private readonly ConcurrentQueue<ST_Sequence> _pending = new ConcurrentQueue<ST_Sequence>();

        private int _writing;   // 1 while a worker is writing the pending entries
        private bool _started;

        public PlcSequenceLog(PlcSubscriptions subscriptions, IEnumerable<string> symbols)
        {
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _symbols = (symbols ?? Enumerable.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_started || _symbols.Count == 0)
                    return;

                _started = true;
            }

            _subscriptions.ValueChanged += OnValueChanged;
            _subscriptions.LiveChanged += OnLiveChanged;
            foreach (string symbol in _symbols)
                _subscriptions.Watch(symbol, typeof(ST_Sequence), CheckEvery);

            ReportAll();
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
            _subscriptions.LiveChanged -= OnLiveChanged;
        }

        /// <summary>The subscriptions are shared and belong to their creator, so they are left running.</summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>Runs on the ADS thread: no ADS calls, and nothing slow.</summary>
        private void OnValueChanged(object sender, string symbol)
        {
            if (_symbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
                Report(symbol);
        }

        /// <summary>
        /// The first value of each log arrives while subscribing, before the values count as
        /// live - so they are taken once the subscription is.
        /// </summary>
        private void OnLiveChanged(object sender, EventArgs e)
        {
            if (_subscriptions.IsLive)
                ReportAll();
        }

        private void ReportAll()
        {
            foreach (string symbol in _symbols)
                Report(symbol);
        }

        private void Report(string symbol)
        {
            ST_Sequence entry;
            if (!_subscriptions.TryGet(symbol, out entry))
                return;

            lock (_sync)
            {
                ST_Sequence last;
                if (!_started || (_last.TryGetValue(symbol, out last) && last.SameAs(entry)))
                    return;

                _last[symbol] = entry;

                // After a PLC restart every log is empty until its sequence runs.
                if (entry.IsEmpty)
                    return;

                // Queued under the lock, so entries go out in the order they were seen.
                _pending.Enqueue(entry);
            }

            if (Interlocked.Exchange(ref _writing, 1) == 0)
                ThreadPool.QueueUserWorkItem(_ => WritePending());
        }

        private void WritePending()
        {
            while (true)
            {
                ST_Sequence entry;
                while (_pending.TryDequeue(out entry))
                    Write(entry);

                Interlocked.Exchange(ref _writing, 0);

                // An entry queued after the last dequeue, whose Report saw this worker still running.
                if (_pending.IsEmpty || Interlocked.Exchange(ref _writing, 1) != 0)
                    return;
            }
        }

        /// <summary>
        /// One entry, as "[time] | Running | station | sequence | Step n - message" in the
        /// running log. In the SequenceLog file the function and line columns hold the
        /// sequence name and step number.
        /// </summary>
        private static void Write(ST_Sequence entry)
        {
            try
            {
                string station = string.IsNullOrWhiteSpace(entry.StationName) ? "Sequence" : entry.StationName.Trim();
                string sequence = (entry.SequenceName ?? "").Trim();
                string text = "Step " + entry.StepNumber + " - " + (entry.Message ?? "").Trim();

                // The library names the log file's logger after this "file"; NLog.config sends
                // loggers named Sequence_* to the SequenceLog files.
                Logger.WriteLog(LogType.Running, station, sequence, text, "", DateTime.Now,
                    sequence, entry.StepNumber, "Sequence_" + FileSafe(station));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not log a PLC sequence entry: " + ex.Message);
            }
        }

        /// <summary>The station name as part of a file name: letters, digits, '-' and '_' only.</summary>
        private static string FileSafe(string name)
        {
            var safe = new StringBuilder(name.Length);
            foreach (char c in name)
                safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');

            return safe.ToString();
        }
    }
}
