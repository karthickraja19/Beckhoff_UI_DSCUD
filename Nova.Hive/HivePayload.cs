using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Nova.Hive
{
    /// <summary>
    /// Builds the JSON that is posted to the HIVE gateway (HIVE PayloadSpec 5.6 and
    /// Audio SW DFM v5.4, "Machine State Payload Definition").
    /// </summary>
    public static class HivePayload
    {
        /// <summary>
        /// A time the way HIVE wants it: "2026-09-22T14:30:05.123+0800".
        /// </summary>
        /// <remarks>
        /// .NET writes the offset as "+08:00"; HIVE wants it without the colon and
        /// rejects the payload otherwise.
        /// </remarks>
        public static string Time(DateTime time)
        {
            string offset = time.ToString("zzz").Replace(":", "");
            return time.ToString("yyyy-MM-dd'T'HH:mm:ss.fff") + offset;
        }

        /// <summary>The machine_state payload for one state change.</summary>
        public static string MachineState(HiveStateChange change, HiveSettings settings)
        {
            var data = new JObject
            {
                ["machine_type"] = settings.MachineType,
                ["previous_state"] = change.PreviousState,
                ["sequence"] = settings.Sequence,
                ["sw_version"] = settings.SoftwareVersion,
            };

            // Slide 12: every configured hash key, each under its own key. A key that could
            // not be read goes out empty - the downtime that follows says why (slide 91).
            if (change.HashKeys != null && change.HashKeys.Count > 0)
            {
                foreach (var key in change.HashKeys)
                {
                    if (!string.IsNullOrWhiteSpace(key.Key))
                        data[key.Key] = key.Value ?? "";
                }
            }
            else
            {
                data["MS_SHA1"] = settings.MainSoftwareHash;
            }

            // S4.1: the 24-hour dry run's id from UMSID.dll.
            if (!string.IsNullOrWhiteSpace(change.UniqueMachineStateId))
                data["unique_machine_state_id"] = change.UniqueMachineStateId;

            // The DFM allows error information on state 5 payloads only.
            if (change.Error != null)
            {
                data["code"] = change.Error.Code;
                data["error_message"] = change.Error.Message;
                data["error_detail"] = change.Error.Detail;
            }

            // Only sent when the operator changed the state by hand.
            if (!string.IsNullOrWhiteSpace(change.OperatorId))
                data["u_id"] = change.OperatorId;

            var payload = new JObject
            {
                ["machine_state"] = change.State,
                ["state_change_time"] = Time(change.Time),
                ["data"] = data,
            };

            return payload.ToString(Formatting.None);
        }

        /// <summary>
        /// The machine_data payload for one finished unit (PayloadSpec 5.6, "parametric /
        /// attribute / limit data"): sent as the <c>jsondata</c> form of a multipart request.
        /// </summary>
        /// <remarks>
        /// "pass" is a string in the spec ("true" / "false"), not a JSON boolean. "blobs" is
        /// always there - an empty list when no file goes with the unit; an empty file name
        /// makes Insight refuse the upload (DFM slide 19).
        /// </remarks>
        public static string MachineData(HiveUnitData unit)
        {
            var serials = new JObject();
            foreach (var pair in unit.Serials)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    serials[pair.Key] = pair.Value;
            }

            var data = new JObject();
            foreach (var pair in unit.Data)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                    data[pair.Key] = JToken.FromObject(pair.Value);
            }

            var payload = new JObject
            {
                ["unit_sn"] = unit.UnitSn,
                ["serials"] = serials,
                ["pass"] = unit.Pass ? "true" : "false",
                ["input_time"] = Time(unit.InputTime),
                ["output_time"] = Time(unit.OutputTime),
                ["data"] = data,
            };

            // A limit only for a key that is really in data ("All keys in limit section
            // must match keys in data section").
            var limits = new JObject();
            foreach (var pair in unit.Limits)
            {
                if (pair.Value == null || data[pair.Key] == null || (!pair.Value.Lower.HasValue && !pair.Value.Upper.HasValue))
                    continue;

                var limit = new JObject();
                if (pair.Value.Upper.HasValue)
                    limit["upper_limit"] = pair.Value.Upper.Value;
                if (pair.Value.Lower.HasValue)
                    limit["lower_limit"] = pair.Value.Lower.Value;
                limits[pair.Key] = limit;
            }

            if (limits.Count > 0)
                payload["limit"] = limits;

            var blobs = new JArray();
            foreach (string file in unit.BlobFiles)
            {
                if (!string.IsNullOrWhiteSpace(file))
                    blobs.Add(new JObject { ["file_name"] = System.IO.Path.GetFileName(file) });
            }

            payload["blobs"] = blobs;

            return payload.ToString(Formatting.None);
        }

        /// <summary>The errordata payload for one alarm that has been cleared.</summary>
        public static string ErrorData(HiveError error, DateTime occurred, DateTime resolved)
        {
            var payload = new JObject
            {
                ["message"] = error.Message,
                ["code"] = error.Code,
                ["severity"] = error.Severity,
                ["occurrence_time"] = Time(occurred),
                ["resolved_time"] = Time(resolved),
                ["data"] = new JObject { ["error_detail"] = error.Detail },
            };

            return payload.ToString(Formatting.None);
        }
    }
}
