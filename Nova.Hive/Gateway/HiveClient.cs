using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    /// The queue is kept in <see cref="HiveSettings.PendingFile"/> as well as in memory:
    /// payloads that have not been sent when the HMI is closed are loaded again at the
    /// next start and sent in their original order, still inside the 3 day limit.
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
            _pendingFile = PendingFilePath(settings);
            LoadPending();
        }

        private readonly string _pendingFile;

        private static string PendingFilePath(HiveSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.PendingFile))
                return settings.PendingFile;

            string folder = string.IsNullOrWhiteSpace(settings.StateFile) ? null : Path.GetDirectoryName(settings.StateFile);
            return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "HivePending.json");
        }

        /// <summary>
        /// The state of the newest machine_state payload still waiting to be sent, or null
        /// when none is waiting. Once the queue is sent, this is the state HIVE ends up in.
        /// </summary>
        public int? LastQueuedState
        {
            get
            {
                lock (_sync)
                {
                    for (int i = _queue.Count - 1; i >= 0; i--)
                    {
                        if (_queue[i].StateChange != null)
                            return _queue[i].StateChange.State;
                    }
                }

                return null;
            }
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
            string json = HivePayload.MachineState(change, _settings);
            WriteRemoteQualificationLog("MachineState", json);
            Queue("machinestate", json, change);
        }

        // ------------------------------------------------------------------
        // machine_data: one payload per finished unit (DFM S1.2, S12)
        // ------------------------------------------------------------------

        /// <summary>
        /// Raised, on a worker thread, once a unit's machine_data payload has its outcome:
        /// accepted, refused by the gateway's own check, or not confirmed in time.
        /// </summary>
        public event EventHandler<HiveUnitResult> UnitDataSent;

        private Task _units = Task.CompletedTask;   // unit uploads, one at a time, in order

        /// <summary>
        /// Sends one finished unit. Returns at once: the upload runs on a worker thread,
        /// after the units before it, so it never holds up the next unit (slide 14: "Success
        /// message wait time does not gate unit from exiting station").
        /// </summary>
        /// <remarks>
        /// The gateway has <see cref="HiveSettings.UnitTimeout"/> to answer 200/201, and the
        /// upload is tried <see cref="HiveSettings.UnitAttempts"/> times (S12). Without a
        /// 200/201 the outcome is <see cref="HiveUnitOutcome.NotConfirmed"/> and the payload
        /// joins the queue, to be sent again every 5 minutes for 3 days - the spec wants it
        /// "resent until successful receipt confirmed". A 4xx is our mistake and is not sent
        /// again.
        /// </remarks>
        public void SendUnit(HiveUnitData unit)
        {
            string json = HivePayload.MachineData(unit);
            WriteRemoteQualificationLog("MachineData", json);

            if (!_settings.Enabled)
            {
                WriteLog("machinedata", json, "not sent - HIVE is switched off");
                RaiseUnitResult(unit, HiveUnitOutcome.NotSent, "HIVE is switched off");
                return;
            }

            var files = new List<string>(unit.BlobFiles.Where(f => !string.IsNullOrWhiteSpace(f)));
            lock (_sync)
            {
                _units = _units.ContinueWith(_ => SendUnitNow(unit, json, files), TaskScheduler.Default);
            }
        }

        private void SendUnitNow(HiveUnitData unit, string json, List<string> files)
        {
            try
            {
                int attempts = Math.Max(1, _settings.UnitAttempts);
                PostResult last = null;

                for (int attempt = 1; attempt <= attempts; attempt++)
                {
                    last = Post("machinedata", json, files, _settings.UnitTimeout);

                    if (last.Success)
                    {
                        string refused = RefusedBecause(last.Answer, unit.IsAudit);
                        RaiseUnitResult(unit, refused == null ? HiveUnitOutcome.Accepted : HiveUnitOutcome.Refused, last.Answer);
                        return;
                    }

                    // 4xx: the payload itself is wrong; the same payload again changes nothing.
                    if (last.IsClientError)
                        break;
                }

                if (last.IsClientError)
                {
                    Logger.WriteLog(LogType.Error, "Hive", "Client",
                        "HIVE refused a machinedata payload (" + last.Describe() + ")", json);
                }
                else
                {
                    Queue("machinedata", json, null, files);
                }

                RaiseUnitResult(unit, HiveUnitOutcome.NotConfirmed, last.Describe());
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "Client", "Sending a unit to HIVE failed: " + ex.Message, ex.ToString());
                RaiseUnitResult(unit, HiveUnitOutcome.NotConfirmed, ex.Message);
            }
        }

        /// <summary>
        /// S12 "HIVE Gateway JSON Response Validation": "Result":"FAIL" - a value out of its
        /// limits, a missing or unknown key - or, with no Result at all, an ErrorCode that
        /// is not null. Null when the gateway found nothing wrong.
        /// </summary>
        /// <remarks>
        /// For an audit unit (S3.4, S3.5) ErrorCode 1024 (unexpected keys such as Mode,
        /// Operator_ID, TestSeriesID) and 1025 (non-numeric pdata) still mean the data was
        /// logged; any other error code is a failed upload.
        /// </remarks>
        private static string RefusedBecause(string answer, bool isAudit)
        {
            try
            {
                JObject parsed = JObject.Parse(answer ?? string.Empty);
                string result = (string)parsed["Result"];

                if (isAudit)
                {
                    JToken auditCode = parsed["ErrorCode"];
                    int number;
                    if (auditCode != null && auditCode.Type != JTokenType.Null
                        && int.TryParse((string)auditCode, out number) && (number == 1024 || number == 1025))
                        return null;
                }

                if (string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (string.Equals(result, "FAIL", StringComparison.OrdinalIgnoreCase))
                    return answer;

                JToken code = parsed["ErrorCode"];
                return code == null || code.Type == JTokenType.Null ? null : answer;
            }
            catch (JsonException)
            {
                // 200/201 with an answer that is not the gateway's JSON: HIVE has the unit.
                return null;
            }
        }

        private void RaiseUnitResult(HiveUnitData unit, HiveUnitOutcome outcome, string detail)
        {
            try
            {
                UnitDataSent?.Invoke(this, new HiveUnitResult { Unit = unit, Outcome = outcome, Detail = detail });
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "Client", "Handling a unit's HIVE result failed: " + ex.Message, ex.ToString());
            }
        }

        /// <summary>
        /// S2.4 Extra Log for HIVE Remote Qualification: while Remote Qualification is ON,
        /// each machine_state and machine_data payload is also written to its own folder
        /// under the HIVE log - plain JSON, one full payload per line. Sequence 0 only: data
        /// from other sequences must not be in this log.
        /// </summary>
        private void WriteRemoteQualificationLog(string kindFolder, string json)
        {
            try
            {
                if (_settings.RemoteQualification == null || !_settings.RemoteQualification() || _settings.Sequence != 0)
                    return;

                string folder = Path.Combine(_settings.LogFolder, "RemoteQualification", kindFolder);
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd") + ".log"), json + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "Log", "Could not write the remote qualification log: " + ex.Message, ex.ToString());
            }
        }

        /// <summary>Queues one cleared alarm for the gateway.</summary>
        public void SendError(HiveError error, DateTime occurred, DateTime resolved)
        {
            Queue("errordata", HivePayload.ErrorData(error, occurred, resolved), null);
        }

        private void Queue(string kind, string json, HiveStateChange change, List<string> files = null)
        {
            // Without a gateway nothing is sent, but the payload is still written to the
            // log so it can be checked on a desk.
            if (!_settings.Enabled)
            {
                WriteLog(kind, json, "not sent - HIVE is switched off");
                return;
            }

            var message = new Message
            {
                Kind = kind,
                Json = json,
                StateChange = change,
                Files = files,
                Created = DateTime.Now,
                // A unit comes here after its first attempts failed: next try in 5 minutes.
                NextTry = kind == "machinedata" ? DateTime.Now + RetryAfter : DateTime.Now,
            };

            lock (_sync)
            {
                _queue.Add(message);
                SavePending();
            }
        }

        /// <summary>Writes the unsent payloads to the pending file. Call with <c>_sync</c> held.</summary>
        private void SavePending()
        {
            if (_pendingFile == null)
                return;

            try
            {
                if (_queue.Count == 0)
                {
                    if (File.Exists(_pendingFile))
                        File.Delete(_pendingFile);
                    return;
                }

                var entries = new List<PendingEntry>();
                foreach (Message message in _queue)
                {
                    entries.Add(new PendingEntry
                    {
                        Kind = message.Kind,
                        Json = message.Json,
                        Created = message.Created,
                        State = message.StateChange != null ? message.StateChange.State : (int?)null,
                        Files = message.Files,
                    });
                }

                Directory.CreateDirectory(Path.GetDirectoryName(_pendingFile));
                string temp = _pendingFile + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(entries, Formatting.Indented));
                if (File.Exists(_pendingFile))
                    File.Delete(_pendingFile);
                File.Move(temp, _pendingFile);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "Client", "Could not write " + _pendingFile + ": " + ex.Message, ex.ToString());
            }
        }

        /// <summary>Loads what was still waiting when the HMI was last closed, oldest first.</summary>
        private void LoadPending()
        {
            if (_pendingFile == null)
                return;

            try
            {
                if (!File.Exists(_pendingFile))
                    return;

                List<PendingEntry> entries = JsonConvert.DeserializeObject<List<PendingEntry>>(File.ReadAllText(_pendingFile));
                if (entries == null)
                    return;

                foreach (PendingEntry entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Kind) || string.IsNullOrWhiteSpace(entry.Json))
                        continue;

                    // Only the state is needed from the change: it is what the state file is
                    // written from once the gateway accepts the payload.
                    _queue.Add(new Message
                    {
                        Kind = entry.Kind,
                        Json = entry.Json,
                        StateChange = entry.State.HasValue ? new HiveStateChange { State = entry.State.Value } : null,
                        Files = entry.Files,
                        Created = entry.Created,
                        NextTry = DateTime.Now,
                    });
                }

                if (_queue.Count > 0)
                    Logger.WriteLog(LogType.Info, "Hive", "Client", _queue.Count + " HIVE payload(s) from the last run are still waiting to be sent.", "");
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "Client", "Could not read " + _pendingFile + ": " + ex.Message, ex.ToString());
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
                Message message = NextDue();
                if (message == null)
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

                    // The gateway is not answering: everything waits, so nothing overtakes.
                    DateTime next = DateTime.Now + RetryAfter;
                    lock (_sync)
                    {
                        foreach (Message waiting in _queue)
                        {
                            if (waiting.NextTry < next)
                                waiting.NextTry = next;
                        }
                    }

                    return;
                }

                if (result == SendResult.Sent && message.StateChange != null)
                    MachineStateSent?.Invoke(this, message.StateChange);

                Remove(message);
            }
        }

        /// <summary>
        /// The oldest payload that is due. State changes and errors keep their order: one
        /// that is not due yet holds back the ones after it. A unit's payload stands on its
        /// own, so a unit waiting for its next try holds nothing back.
        /// </summary>
        private Message NextDue()
        {
            DateTime now = DateTime.Now;
            lock (_sync)
            {
                bool orderedHeld = false;
                foreach (Message message in _queue)
                {
                    bool isUnit = message.Kind == "machinedata";
                    if (!isUnit && orderedHeld)
                        continue;

                    if (message.NextTry <= now)
                        return message;

                    if (!isUnit)
                        orderedHeld = true;
                }
            }

            return null;
        }

        private SendResult TrySend(Message message)
        {
            PostResult result = Post(message.Kind, message.Json, message.Files, null);

            if (result.Success)
                return SendResult.Sent;

            // 4xx means the payload itself is wrong. The HIVE spec says to correct
            // it rather than send it again, so it is dropped and left in the log.
            if (result.IsClientError)
            {
                Logger.WriteLog(LogType.Error, "Hive", "Client",
                    "HIVE refused a " + message.Kind + " payload (" + result.Describe() + ")", message.Json);
                return SendResult.Rejected;
            }

            // Gateway unreachable, timed out or 5xx: try again later.
            return SendResult.Failed;
        }

        /// <summary>
        /// One POST to the gateway, written to the HIVE log with its answer. machine_data
        /// goes as multipart/form-data (PayloadSpec 5.6; DFM slide 14 rule 5), the other
        /// kinds as JSON.
        /// </summary>
        /// <param name="timeout">How long to wait for the answer; null for the client's own 10 seconds.</param>
        private PostResult Post(string kind, string json, IList<string> files, TimeSpan? timeout)
        {
            string url = _settings.GatewayUrl.TrimEnd('/') + "/v5/capture/" + kind;

            try
            {
                if (files != null)
                {
                    string missing = files.FirstOrDefault(f => !File.Exists(f));
                    if (missing != null)
                    {
                        WriteLog(kind, json, "not sent - blob file not found: " + missing);
                        return new PostResult { Error = "blob file not found: " + missing, IsClientError = true };
                    }
                }

                using (HttpContent content = kind == "machinedata"
                    ? MultipartContent(json, files)
                    : new StringContent(json, Encoding.UTF8, "application/json"))
                using (var cancel = new CancellationTokenSource())
                {
                    if (timeout.HasValue)
                        cancel.CancelAfter(timeout.Value);

                    using (HttpResponseMessage response = _http.PostAsync(url, content, cancel.Token).GetAwaiter().GetResult())
                    {
                        string answer = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        int status = (int)response.StatusCode;

                        WriteLog(kind, json, status + ", " + answer);

                        return new PostResult
                        {
                            Status = status,
                            Answer = answer,
                            Success = response.IsSuccessStatusCode,
                            IsClientError = status >= 400 && status < 500,
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                string why = ex is OperationCanceledException && timeout.HasValue
                    ? "no 200/201 within " + timeout.Value.TotalSeconds.ToString("0.#") + " s"
                    : ex.GetBaseException().Message;

                WriteLog(kind, json, "no answer: " + why);
                return new PostResult { Error = why };
            }
        }

        /// <summary>
        /// PayloadSpec 5.6: "The json content should be sent in one form with jsondata as
        /// the key. Every file should be sent in an independent form with a different name
        /// like file1, file2, ...". Written the way curl -F writes it: quoted form names,
        /// no quotes round the boundary, no content type on the JSON part.
        /// </summary>
        private static HttpContent MultipartContent(string json, IList<string> files)
        {
            var content = new MultipartFormDataContent("----HiveBoundary" + Guid.NewGuid().ToString("N"));

            NameValueHeaderValue boundary = content.Headers.ContentType.Parameters.First(p => p.Name == "boundary");
            boundary.Value = boundary.Value.Trim('"');

            var jsonPart = new StringContent(json, Encoding.UTF8);
            jsonPart.Headers.ContentType = null;
            content.Add(jsonPart, "\"jsondata\"");

            if (files != null)
            {
                for (int i = 0; i < files.Count; i++)
                {
                    var filePart = new StreamContent(File.OpenRead(files[i]));
                    filePart.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    content.Add(filePart, "\"file" + (i + 1) + "\"", "\"" + Path.GetFileName(files[i]) + "\"");
                }
            }

            return content;
        }

        /// <summary>What one POST came back with.</summary>
        private sealed class PostResult
        {
            /// <summary>The HTTP status; 0 when there was no answer.</summary>
            public int Status;
            public string Answer;
            public string Error;

            /// <summary>200/201 (any 2xx).</summary>
            public bool Success;

            /// <summary>4xx, or a payload that could not be put together: not to be sent again as it is.</summary>
            public bool IsClientError;

            public string Describe()
            {
                return Status > 0 ? Status + ", " + Answer : Error;
            }
        }

        private void Remove(Message message)
        {
            lock (_sync)
            {
                _queue.Remove(message);
                SavePending();
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
                    // Machine Data and Machine Error: a folder per day, a file per hour.
                    string kindFolder = kind == "machinedata" ? "MachineData" : "MachineError";
                    string folder = Path.Combine(_settings.LogFolder, kindFolder, now.ToString("yyyyMMdd"));
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

        /// <summary>One unsent payload as it is kept in the pending file.</summary>
        private sealed class PendingEntry
        {
            public string Kind { get; set; }
            public string Json { get; set; }
            public DateTime Created { get; set; }

            /// <summary>The state of a machine_state payload; null for any other kind.</summary>
            public int? State { get; set; }

            /// <summary>The blob files of a machine_data payload, by full path; null for none.</summary>
            public List<string> Files { get; set; }
        }

        private sealed class Message
        {
            public string Kind;
            public string Json;
            public HiveStateChange StateChange;
            public List<string> Files;
            public DateTime Created;
            public DateTime NextTry;
        }
    }
}
