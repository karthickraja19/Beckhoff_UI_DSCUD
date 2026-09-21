using System;
using System.Runtime.CompilerServices;

namespace Nova.Abstractions.Logging
{
    /// <summary>
    /// Core logging interface for Nova components.
    /// Replaces the static Logger from Teal.Library.Log.
    /// </summary>
    public interface INovaLogger
    {
        /// <summary>
        /// Log function to determine the file type to be logged based on configuration
        /// </summary>
        void WriteLog(LogType logType, string moduleName, string stationName, string errMsg, string errorMessage = "", DateTime errorTime = default, 
            [CallerMemberName] string methodName = "", [CallerLineNumber] int lineNumber = 0, [CallerFilePath] string filePath = "");

        /// <summary>
        /// Show the message by adding it to the UI queue
        /// </summary>
        void AddTotheArrayList(string sloginfo);

        /// <summary>
        /// Add to running log list
        /// </summary>
        void AddRunningLoglist(string list, bool wlog = true);
    }
}
