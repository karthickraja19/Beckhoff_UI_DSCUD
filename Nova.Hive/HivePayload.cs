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
