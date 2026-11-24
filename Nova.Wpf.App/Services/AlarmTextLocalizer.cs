using System;
using System.Text;
using System.Text.RegularExpressions;
using Teal.Library.Localization;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// Alarm messages and details in the language chosen on the Setting page.
    ///
    /// The text comes only from the alarm entries of Localization\Strings*.resx - the
    /// description written for that error code - never from a machine translator:
    ///   * Alarm_&lt;PLC code&gt;          the description of one PLC error code, e.g. Alarm_TEAL_MASTER_014;
    ///   * AlarmDesc_&lt;PLC description&gt; the same by the PLC's own text, e.g. AlarmDesc_Emg_Pressed_Error;
    ///   * AlarmType_&lt;error type&gt;     the DFM error types, e.g. AlarmType_Motion_Error.
    /// They are edited on Setting &gt; Localization Editor (search "Alarm"). A code with no
    /// entry in the selected language shows as the PLC and HiveErrorCodes.csv give it.
    ///
    /// Only what is shown changes. The alarm database, the CSV export and everything sent
    /// to HIVE keep the English text.
    /// </summary>
    internal static class AlarmTextLocalizer
    {
        // "... [PLC TEAL_MASTER_014]" - the Alarm page appends the PLC code to the detail.
        private static readonly Regex PlcSuffix = new Regex(@"\s*\[PLC ([^\]]+)\]\s*$", RegexOptions.Compiled);

        // "MACHINE / ESTOP_FRONT : Emg_Pressed_Error" - where and what, as the PLC reports it.
        private static readonly Regex DetailShape = new Regex(@"^(?<head>.+?\s:\s)(?<desc>.+)$", RegexOptions.Compiled | RegexOptions.Singleline);

        public static string Translate(string text, string plcCode)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            try
            {
                if (!LanguageManager.IsTranslated)
                    return text;

                string body = text;
                string suffix = string.Empty;

                Match plc = PlcSuffix.Match(text);
                if (plc.Success)
                {
                    body = text.Substring(0, plc.Index);
                    suffix = plc.Value;
                    if (string.IsNullOrWhiteSpace(plcCode))
                        plcCode = plc.Groups[1].Value.Trim();
                }

                string found = Find("AlarmType_", body);

                if (found == null)
                {
                    Match detail = DetailShape.Match(body);
                    if (detail.Success)
                    {
                        // Station and component names stay; the description is replaced.
                        string description = ByCode(plcCode) ?? Find("AlarmDesc_", detail.Groups["desc"].Value);
                        if (description != null)
                            found = detail.Groups["head"].Value + description;
                    }
                    else
                    {
                        found = ByCode(plcCode) ?? Find("AlarmDesc_", body);
                    }
                }

                // Fixed wording around alarms ("3 alarm(s), 12 min of downtime"), from the
                // language files too.
                if (found == null)
                    found = UiTextTranslator.Translate(body);

                return found + suffix;
            }
            catch (Exception)
            {
                return text;
            }
        }

        private static string ByCode(string plcCode)
        {
            return string.IsNullOrWhiteSpace(plcCode) ? null : Find("Alarm_", plcCode);
        }

        private static string Find(string prefix, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string key = prefix + KeyPart(value.Trim());
            if (key.Length > 200 || !LocalizationProvider.IsTranslated(key))
                return null;

            string text = LocalizationProvider.Get(key);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        /// <summary>Letters and digits kept, everything else an underscore - a valid resx key.</summary>
        private static string KeyPart(string value)
        {
            var key = new StringBuilder(value.Length);
            foreach (char c in value)
                key.Append(c < 128 && char.IsLetterOrDigit(c) ? c : '_');
            return key.ToString();
        }
    }
}
