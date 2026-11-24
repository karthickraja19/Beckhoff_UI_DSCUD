using System;
using Teal.Library.Log;

namespace Master.Sequences.FiveAxis
{
    /// <summary>
    /// The 5 axis calibration's log lines. A log that cannot be written (the logger not set up, a full disk)
    /// must never end or hang a calibration, so nothing here throws.
    /// </summary>
    internal static class FiveAxisLog
    {
        public static void Info(string area, string text)
        {
            try
            {
                Logger.WriteLog(LogType.Info, "FiveAxis", area, text, string.Empty);
            }
            catch (Exception)
            {
            }
        }

        public static void Error(string area, string text, string detail)
        {
            try
            {
                Logger.WriteLog(LogType.Error, "FiveAxis", area, text, detail ?? string.Empty);
            }
            catch (Exception)
            {
            }
        }
    }
}
