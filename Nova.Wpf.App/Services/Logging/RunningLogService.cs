using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using Nova.Abstractions.Services;
using TealLogger = Teal.Library.Log.Logger;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// Drains Teal.Library.Log.Logger.logQueue into the UI, mirroring V3's
    /// HomeScreen.ProcessLogInBackgroud(): a 1s poll, at most 50 lines per pass, with
    /// a re-entrancy guard so a slow UI tick can't stack up overlapping drains.
    ///
    /// V3 drained on a background Task and marshalled the list back with
    /// BeginInvoke; this keeps that shape — the dequeue runs on a timer thread, the
    /// event is raised on the dispatcher — so subscribers can touch bound
    /// ObservableCollections without cross-thread violations.
    /// </summary>
    public sealed class RunningLogService : IRunningLogService, IDisposable
    {
        private const int PollMs = 1000;
        private const int MaxBatch = 50;

        private readonly object _sync = new object();
        private Timer _timer;
        private int _draining;

        public event EventHandler<IReadOnlyList<string>> LogsAppended;

        public void Start()
        {
            lock (_sync)
            {
                if (_timer == null)
                    _timer = new Timer(OnTick, null, PollMs, PollMs);
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        public void Dispose() => Stop();

        private void OnTick(object state)
        {
            // V3's dataBindingInProgress guard: skip this pass rather than queue behind
            // one still finishing.
            if (Interlocked.Exchange(ref _draining, 1) == 1)
                return;

            try
            {
                var batch = new List<string>();
                string log;
                while (batch.Count < MaxBatch && TealLogger.logQueue.TryDequeue(out log))
                {
                    if (!string.IsNullOrEmpty(log))
                        batch.Add(log);
                }

                if (batch.Count == 0)
                    return;

                var app = Application.Current;
                if (app == null)
                    return; // shutting down

                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        LogsAppended?.Invoke(this, batch);
                    }
                    catch
                    {
                        // A faulty subscriber must not kill the log pump.
                    }
                }));
            }
            catch
            {
                // Logging must never take the app down.
            }
            finally
            {
                Interlocked.Exchange(ref _draining, 0);
            }
        }
    }
}
