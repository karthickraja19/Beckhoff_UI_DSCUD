namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Starts Beckhoff's CNC HMI (Beckhoff.App.Shell.Core.exe), a separate program with its own
    /// window and its own ADS connection to the PLC. Where it is installed is per machine
    /// (App.config CncHmiExe), so the path is always the launcher's, never a view model's.
    /// </summary>
    public interface ICncHmiLauncher
    {
        /// <summary>The full path of the CNC HMI's exe on this machine, found or not.</summary>
        string ExePath { get; }

        /// <summary>True when <see cref="ExePath"/> exists on disk.</summary>
        bool IsInstalled { get; }

        /// <summary>
        /// Starts the CNC HMI, or brings it to the front when it is already running (it has no
        /// guard against a second copy of its own). Never throws: a missing exe or a failed
        /// start comes back as a result whose message says what to do.
        /// </summary>
        CncHmiLaunchResult Launch();
    }

    /// <summary>What a <see cref="ICncHmiLauncher.Launch"/> did.</summary>
    public sealed class CncHmiLaunchResult
    {
        /// <summary>True when the CNC HMI is now running (started, or was already).</summary>
        public bool Success { get; private set; }

        /// <summary>Said to the operator when <see cref="Success"/> is false; a note for the log otherwise.</summary>
        public string Message { get; private set; }

        /// <summary>True when the CNC HMI was running already and was only brought forward.</summary>
        public bool WasRunning { get; private set; }

        public static CncHmiLaunchResult Started(string message)
        {
            return new CncHmiLaunchResult { Success = true, Message = message };
        }

        public static CncHmiLaunchResult AlreadyRunning(string message)
        {
            return new CncHmiLaunchResult { Success = true, WasRunning = true, Message = message };
        }

        public static CncHmiLaunchResult Failed(string message)
        {
            return new CncHmiLaunchResult { Success = false, Message = message };
        }
    }
}
