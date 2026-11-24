using System;
using System.Globalization;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nova.Abstractions.Services;
using Nova.Hive;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// Reports every HIVE state change to MES as well (S1.1.2 Machine State Reporting to MES
    /// V5.0, <c>/api/machine/collmachinestatefromcard</c>).
    /// </summary>
    /// <remarks>
    /// What each state carries:
    /// <list type="bullet">
    /// <item>5 (Downtime): the error - message, code, serverity, error_Detail - with
    /// occurrence_Time the start of the downtime and resolved_Time empty. A 5 to 5 change
    /// (a new reason, an acknowledged alarm) keeps the start of the downtime.</item>
    /// <item>1 (Running) out of downtime: the downtime's error again, occurrence_Time its
    /// start, resolved_Time now.</item>
    /// <item>1 from Idle, and 2 (Idle): occurrence_Time the change time, resolved_Time and the
    /// error fields empty.</item>
    /// </list>
    /// uid only on a change made by hand. Reports go out one at a time, in order, off the
    /// thread that changed the state; a false answer or no answer is written to the machine
    /// log and nothing is sent again.
    /// </remarks>
    public sealed class MesStateReporter : IDisposable
    {
        private const string MesTime = "yyyy-M-d HH:mm:ss";

        private readonly HiveService _hive;
        private readonly IMesClient _mes;
        private readonly Func<string> _machineName;
        private readonly Func<string> _vendor;
        private readonly Func<string> _line;
        private readonly object _gate = new object();

        private Task _posts = Task.CompletedTask;
        private DateTime? _downtimeStart;
        private HiveError _downtimeError;

        /// <param name="machineName">The ICT process station id, e.g. "BE010" (machine_Name, and the start of uniqueID).</param>
        /// <param name="vendor">The vendor's English name; sent in capitals.</param>
        /// <param name="line">The CM line, e.g. "R1" (pdline_name).</param>
        public MesStateReporter(HiveService hive, IMesClient mes, Func<string> machineName, Func<string> vendor, Func<string> line)
        {
            _hive = hive ?? throw new ArgumentNullException(nameof(hive));
            _mes = mes ?? throw new ArgumentNullException(nameof(mes));
            _machineName = machineName ?? (() => string.Empty);
            _vendor = vendor ?? (() => string.Empty);
            _line = line ?? (() => string.Empty);
        }

        public void Start()
        {
            if (!_mes.IsConfigured)
            {
                Logger.WriteLog(LogType.Info, "MES", "MachineState", "MES is not configured: HIVE state changes are not reported to MES.", string.Empty);
                return;
            }

            _hive.StateChangeSent += OnStateChangeSent;
        }

        public void Dispose()
        {
            _hive.StateChangeSent -= OnStateChangeSent;
        }

        private void OnStateChangeSent(object sender, HiveStateChange change)
        {
            string json;
            lock (_gate)
            {
                json = Build(change);
                _posts = _posts.ContinueWith(_ => Post(json), TaskScheduler.Default).Unwrap();
            }
        }

        /// <summary>The report for one change. Call in the order the changes happen.</summary>
        internal string Build(HiveStateChange change)
        {
            HiveError error = null;
            DateTime occurred = change.Time;
            string resolved = string.Empty;

            if (change.State == HiveStateMachine.Downtime)
            {
                if (change.PreviousState != HiveStateMachine.Downtime || _downtimeStart == null)
                    _downtimeStart = change.Time;

                _downtimeError = change.Error ?? _downtimeError;
                error = _downtimeError;
                occurred = _downtimeStart.Value;
            }
            else
            {
                if (change.State == HiveStateMachine.Running && change.PreviousState == HiveStateMachine.Downtime && _downtimeStart != null)
                {
                    error = _downtimeError;
                    occurred = _downtimeStart.Value;
                    resolved = change.Time.ToString(MesTime, CultureInfo.InvariantCulture);
                }

                _downtimeStart = null;
                _downtimeError = null;
            }

            string machine = (_machineName() ?? string.Empty).Trim();

            var data = new JObject
            {
                ["sW_Version"] = _hive.Settings.SoftwareVersion ?? string.Empty,
                ["code"] = error != null ? error.Code ?? string.Empty : string.Empty,
                ["error_Detail"] = error != null ? error.Detail ?? string.Empty : string.Empty,
            };

            // The hash keys as sent to HIVE, named as MES writes them: mS_SHA1, vS_SHA1, cD_SHA1.
            if (change.HashKeys != null)
            {
                foreach (HashKeyReading key in change.HashKeys)
                {
                    if (!string.IsNullOrWhiteSpace(key.Key))
                        data[LowerFirst(key.Key)] = key.Value ?? string.Empty;
                }
            }

            data["sequence"] = _hive.Settings.Sequence.ToString(CultureInfo.InvariantCulture);

            var body = new JObject
            {
                ["uniqueID"] = machine + "_" + change.Time.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture),
                ["terminal_Name"] = _mes.TerminalName,
                ["machine_Name"] = machine,
                ["machine_State"] = change.State.ToString(CultureInfo.InvariantCulture),
                ["state_Change_Time"] = change.Time.ToString(MesTime, CultureInfo.InvariantCulture),
                ["message"] = error != null ? error.Message ?? string.Empty : string.Empty,
                ["code"] = error != null ? error.Code ?? string.Empty : string.Empty,
                ["serverity"] = error != null ? error.Severity ?? string.Empty : string.Empty,
                ["occurrence_Time"] = occurred.ToString(MesTime, CultureInfo.InvariantCulture),
                ["resolved_Time"] = resolved,
                ["vendor"] = (_vendor() ?? string.Empty).Trim().ToUpperInvariant(),
                // Only when the state was changed by hand.
                ["uid"] = change.OperatorId ?? string.Empty,
                ["pdline_name"] = (_line() ?? string.Empty).Trim(),
                ["data"] = data,
            };

            return body.ToString(Formatting.None);
        }

        private async Task Post(string json)
        {
            try
            {
                MesResult result = await _mes.PostMachineStateAsync(json).ConfigureAwait(false);
                if (!result.Reached || !result.Ok)
                {
                    Logger.WriteLog(LogType.Error, "MES", "MachineState",
                        "[Error/Alert] MES did not take the machine state: " + result.Message, json);
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "MES", "MachineState", "Reporting the machine state to MES failed: " + ex.Message, ex.ToString());
            }
        }

        private static string LowerFirst(string key)
        {
            return key.Length == 0 ? key : char.ToLowerInvariant(key[0]) + key.Substring(1);
        }
    }
}
