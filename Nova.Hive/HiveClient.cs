using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Hive
{
    /// <summary>
    /// Sends payloads to the HIVE gateway and writes the HIVE log.
    /// </summary>
    /// <remarks>
    /// Payloads wait in a queue and are sent in the order they happened, so HIVE sees
    /// the state changes in the right order. The HIVE spec says what to do when a send
    /// fails: a 4xx answer is our mistake, so the payload is dropped and logged instead
    /// of being sent again; anything else is tried again every 5 minutes for 3 days.
    ///
    /// The queue is only in memory, so payloads that have not been sent are lost if the
    /// HMI is closed.
    /// </remarks>
    public sealed class HiveClient : IDisposable
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromDays(3);

        private readonly HiveSettings _settings;
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private readonly List<Message> _queue = new List<Message>();
        private readonly object _sync = new object();

        private Timer _timer;
        private int _sending;

        /// <summary>Raised after the gateway accepted a machine_state payload.</summary>
        public event EventHandler<HiveStateChange> MachineStateSent;

        public HiveClient(HiveSettings settings)
        {
            _settings = settings;
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_timer == null)
                    _timer = new Timer(OnTimer, null, CheckInterval, CheckInterval);
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _http.Dispose();
        }

        /// <summary>Queues one state change for the gateway.</summary>
        public void Send(HiveStateChange change)
        {
            Queue("machinestate", HivePayload.MachineState(change, _settings), change);
        }

        /// <summary>Queues one cleared alarm for the gateway.</summary>
        public void SendError(HiveError error, DateTime occurred, DateTime resolved)
        {
            Queue("errordata", HivePayload.ErrorData(error, occurred, resolved), null);
        }

        private void Queue(string kind, string json, HiveStateChange change)
        {
            // Without a gateway nothing is sent, but the payload is still written to the
            // log so it can be checked on a desk.
            if (!_settings.Enabled)
            {
                WriteLog(kind, json, "not sent - HIVE is switched off in App.config");
                return;
            }

            var message = new Message
            {
                Kind = kind,
                Json = json,
                StateChange = change,
                Created = DateTime.Now,
                NextTry = DateTime.Now,
            };

            lock (_sync)
            {
                _queue.Add(message);
            }
        }

        private void OnTimer(object state)
        {
            // Skip this tick if the previous one is still sending.
            if (Interlocked.Exchange(ref _sending, 1) == 1)
                return;

            try
            {
                SendQueue();
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "Client", "Sending to HIVE failed: " + ex.Message, ex.ToString());
            }
            finally
            {
                Interlocked.Exchange(ref _sending, 0);
            }
        }

        /// <summary>
        /// Sends what is due, oldest first, and stops at the first one that will not go
        /// through, so the gateway never sees a later state before an earlier one.
        /// </summary>
        private void SendQueue()
        {
            while (true)
            {
                Message message;
                lock (_sync)
                {
                    message = _queue.Count > 0 ? _queue[0] : null;
                }

                if (message == null || message.NextTry > DateTime.Now)
                    return;

                SendResult result = TrySend(message);

                if (result == SendResult.Failed)
                {
                    if (DateTime.Now - message.Created > GiveUpAfter)
                    {
                        WriteLog(message.Kind, message.Json, "given up after 3 days");
                        Remove(message);
                        continue;
                    }

                    message.NextTry = DateTime.Now + RetryAfter;
                    return;
                }

                if (result == SendResult.Sent && message.StateChange != null)
                    MachineStateSent?.Invoke(this, message.StateChange);

                Remove(message);
            }
        }

        private SendResult TrySend(Message message)
        {
            string url = _settings.GatewayUrl.TrimEnd('/') + "/v5/capture/" + message.Kind;

            try
            {
                using (var content = new StringContent(message.Json, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = _http.PostAsync(url, content).Result)
                {
                    string answer = response.Content.ReadAsStringAsync().Result;
                    int status = (int)response.StatusCode;

                    WriteLog(message.Kind, message.Json, status + ", " + answer);

                    if (response.IsSuccessStatusCode)
                        return SendResult.Sent;

                    // 4xx means the payload itself is wrong. The HIVE spec says to correct
                    // it rather than send it again, so it is dropped and left in the log.
                    if (status >= 400 && status < 500)
                    {
                        Logger.WriteLog(LogType.Error, "Hive", "Client",
                            "HIVE refused a " + message.Kind + " payload (" + status + "): " + answer, message.Json);
                        return SendResult.Rejected;
                    }

                    return SendResult.Failed;
                }
            }
            catch (Exception ex)
            {
                // Gateway unreachable or timed out: try again later.
                WriteLog(message.Kind, message.Json, "no answer: " + ex.Message);
                return SendResult.Failed;
            }
        }

        private void Remove(Message message)
        {
            lock (_sync)
            {
                _queue.Remove(message);
            }
        }

        /// <summary>
        /// Writes one line to the HIVE log: what was sent and what came back.
        /// Machine states go into one file per day, errors into one file per hour
        /// (Audio SW DFM v5.4, S4).
        /// </summary>
        private void WriteLog(string kind, string json, string answer)
        {
            try
            {
                DateTime now = DateTime.Now;
                string file;

                if (kind == "machinestate")
                {
                    string folder = Path.Combine(_settings.LogFolder, "MachineState");
                    Directory.CreateDirectory(folder);
                    file = Path.Combine(folder, now.ToString("yyyyMMdd") + ".log");
                }
                else
                {
                    string folder = Path.Combine(_settings.LogFolder, "MachineError", now.ToString("yyyyMMdd"));
                    Directory.CreateDirectory(folder);
                    file = Path.Combine(folder, "hour_" + now.ToString("HH") + ".log");
                }

                File.AppendAllText(file,
                    now.ToString("yyyy/MM/dd HH:mm:ss.fff") + " " + json + " -> " + answer + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "Log", "Could not write the HIVE log: " + ex.Message, ex.ToString());
            }
        }

        private enum SendResult
        {
            Sent,
            Rejected,
            Failed,
        }

        private sealed class Message
        {
            public string Kind;
            public string Json;
            public HiveStateChange StateChange;
            public DateTime Created;
            public DateTime NextTry;
        }
    }
}
