using System;

namespace Nova.Abstractions.Logging
{
    /// <summary>
    /// LogType to identify the type of logs and errors
    /// </summary>
    public enum LogType
    {
        Info,
        Running,
        Error,
        AlarmError,
        RunningLog
    }
}
