using System;
using Nova.Abstractions.Logging;

// TODO: [MIGRATION - TECH DEBT] 
// This static Logger class is a temporary bridge adapter. 
// It was created ONLY because legacy files in Nova.CommAPI (which cannot reference Nova.Core directly)
// still rely on static Logger.WriteLog() calls. 
// Once the CommAPI files are fully refactored to use pure constructor injection for INovaLogger,
// THIS ENTIRE STATIC CLASS MUST BE DELETED to maintain pure DI architecture.

namespace Nova.Abstractions.Legacy
{
    public static class Logger
    {
        public static INovaLogger? Current { get; set; }

        public static void WriteLog(LogType logType, string moduleName, string stationName, string errMsg, string errorMessage = "", DateTime errorTime = default)
        {
            Current?.WriteLog(logType, moduleName, stationName, errMsg, errorMessage, errorTime);
        }

        public static void WriteLog(LogType logType, string moduleName, string stationName, string errMsg, string errorMessage, DateTime errorTime, string methodName, int lineNumber)
        {
            Current?.WriteLog(logType, moduleName, stationName, errMsg, errorMessage, errorTime, methodName, lineNumber, "");
        }
    }
}
