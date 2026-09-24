using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nova.Abstractions.Services;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Hive
{
    /// <summary>
    /// The HIVE state changes, read back from the HIVE log that <see cref="HiveClient"/>
    /// writes - one file a day under MachineState, one line per payload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The log holds every state change the machine made, whether the gateway took it,
    /// refused it or was switched off, which is what the Home page's State Changes
    /// chart needs: the time the machine spent in each state, not what HIVE received.
    /// </para>
    /// <para>
    /// A payload the gateway did not answer is logged again on every retry, so lines
    /// are de-duplicated on state change time and state.
    /// </para>
    /// </remarks>
    public sealed class HiveLogStateHistory : IHiveStateHistory
    {
        /// <summary>How far back to look for the state the machine was in at the start.</summary>
        private const int LookBackDays = 30;

        private readonly string _folder;

        public HiveLogStateHistory(string hiveLogFolder)
        {
            _folder = Path.Combine(hiveLogFolder ?? string.Empty, "MachineState");
        }

        public Task<IReadOnlyList<HiveStateChangeRecord>> GetStateChangesAsync(DateTime start, DateTime end)
        {
            return Task.Run<IReadOnlyList<HiveStateChangeRecord>>(() =>
            {
                var inRange = new List<HiveStateChangeRecord>();
                for (DateTime day = start.Date; day <= end.Date; day = day.AddDays(1))
                    inRange.AddRange(ReadDay(day));

                List<HiveStateChangeRecord> result = inRange
                    .Where(r => r.Time >= start && r.Time <= end)
                    .ToList();

                // The state at the start is the last change before it, which may be days
                // earlier on a machine that sat in one state.
                HiveStateChangeRecord before = inRange.Where(r => r.Time < start).OrderBy(r => r.Time).LastOrDefault();
                for (int back = 1; before == null && back <= LookBackDays; back++)
                    before = ReadDay(start.Date.AddDays(-back)).OrderBy(r => r.Time).LastOrDefault();

                if (before != null)
                    result.Add(before);

                return result
                    .GroupBy(r => new { r.Time, r.State })
                    .Select(g => g.First())
                    .OrderBy(r => r.Time)
                    .ToList();
            });
        }

        private IEnumerable<HiveStateChangeRecord> ReadDay(DateTime day)
        {
            string file = Path.Combine(_folder, day.ToString("yyyyMMdd") + ".log");
            if (!File.Exists(file))
                return Enumerable.Empty<HiveStateChangeRecord>();

            var records = new List<HiveStateChangeRecord>();
            try
            {
                // Shared read: HiveClient may be appending to today's file.
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        HiveStateChangeRecord record = Parse(line);
                        if (record != null)
                            records.Add(record);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "StateHistory", "Could not read " + file + ": " + ex.Message, ex.ToString());
            }

            return records;
        }

        /// <summary>
        /// One log line: "yyyy/MM/dd HH:mm:ss.fff {payload} -> answer". The payload is
        /// read as the first JSON object on the line.
        /// </summary>
        private static HiveStateChangeRecord Parse(string line)
        {
            int brace = line.IndexOf('{');
            if (brace < 0)
                return null;

            try
            {
                JObject payload;
                using (var reader = new JsonTextReader(new StringReader(line.Substring(brace))))
                {
                    // Keep state_change_time as the text HIVE was sent. Left to its
                    // default, Json.NET turns it into a DateTime, converted to this PC's
                    // offset, and the exact parse below no longer matches it.
                    reader.DateParseHandling = DateParseHandling.None;
                    payload = JObject.Load(reader);
                }

                int? state = (int?)payload["machine_state"];
                string time = (string)payload["state_change_time"];
                if (!state.HasValue || string.IsNullOrEmpty(time) || time.Length < 23)
                    return null;

                // "2026-09-24T14:30:05.123+0800": the time is local already, so the offset
                // is not needed.
                DateTime when;
                if (!DateTime.TryParseExact(time.Substring(0, 23), "yyyy-MM-dd'T'HH:mm:ss.fff",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                {
                    return null;
                }

                JToken data = payload["data"];
                return new HiveStateChangeRecord
                {
                    Time = when,
                    State = state.Value,
                    Code = data != null ? (string)data["code"] : null
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
