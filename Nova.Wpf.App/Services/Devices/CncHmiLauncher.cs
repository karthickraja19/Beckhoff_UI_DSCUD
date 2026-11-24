using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Nova.Abstractions.Services;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// Starts Beckhoff's CNC HMI as a program of its own. It is a 32 bit WPF shell with its own
    /// ADS connection and settings (System\GeneralSettings.xml, Log\) kept in its own folder, so
    /// it is not hosted in this window: it runs beside it, started with that folder as its
    /// working directory.
    ///
    /// Where it is installed is per machine. App.config CncHmiExe is its exe, or the folder
    /// holding it, as a full path or relative to this exe, with environment variables
    /// expanded. Blank = a CncHmi folder beside the machine's data folder (C:\TEAL\CncHmi when
    /// the data folder is C:\TEAL\Data).
    ///
    /// The CNC HMI does not stop a second copy of itself from starting, so a press while it is
    /// running brings the running one to the front instead, and presses just after a start are
    /// ignored while its window is still coming up.
    /// </summary>
    public sealed class CncHmiLauncher : ICncHmiLauncher
    {
        public const string ExeName = "Beckhoff.App.Shell.Core.exe";
        public const string DefaultFolderName = "CncHmi";

        /// <summary>How long after a start further presses are ignored: its splash screen and plugins take a while to show a window.</summary>
        private static readonly TimeSpan StartCooldown = TimeSpan.FromSeconds(8);

        private readonly object _sync = new object();
        private DateTime _lastStartUtc = DateTime.MinValue;

        public string ExePath { get; }

        public bool IsInstalled => File.Exists(ExePath);

        public CncHmiLauncher(string configuredPath, string dataDirectory, string appBaseDirectory = null)
        {
            ExePath = Resolve(configuredPath, dataDirectory, appBaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory);
        }

        /// <summary>The CNC HMI's exe on this machine, from App.config's CncHmiExe (see the class remarks).</summary>
        public static string Resolve(string configuredPath, string dataDirectory, string appBaseDirectory)
        {
            string path = (configuredPath ?? string.Empty).Trim();

            if (path.Length == 0)
            {
                string root = null;
                if (!string.IsNullOrWhiteSpace(dataDirectory))
                    root = Directory.GetParent(dataDirectory.TrimEnd('\\', '/'))?.FullName;

                path = Path.Combine(root ?? appBaseDirectory, DefaultFolderName);
            }
            else
            {
                path = Environment.ExpandEnvironmentVariables(path);
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(appBaseDirectory, path);
            }

            path = Path.GetFullPath(path);

            // A folder (existing, or any path that does not name an .exe) holds the exe.
            if (Directory.Exists(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(path, ExeName);

            return path;
        }

        public CncHmiLaunchResult Launch()
        {
            lock (_sync)
            {
                if (!File.Exists(ExePath))
                {
                    return Fail("The CNC HMI was not found at " + ExePath + ". Install it there, or set CncHmiExe in App.config "
                        + "to where it is on this machine.");
                }

                Process running = FindRunning();
                if (running != null)
                {
                    using (running)
                    {
                        string note = BringToFront(running)
                            ? "The CNC HMI is already running; it was brought to the front."
                            : "The CNC HMI is already running.";
                        Log(LogType.Info, note);
                        return CncHmiLaunchResult.AlreadyRunning(note);
                    }
                }

                if (DateTime.UtcNow - _lastStartUtc < StartCooldown)
                    return CncHmiLaunchResult.AlreadyRunning("The CNC HMI is starting.");

                try
                {
                    var info = new ProcessStartInfo(ExePath)
                    {
                        // It sets its own working directory, but its settings and plugins are found beside it.
                        WorkingDirectory = Path.GetDirectoryName(ExePath),
                        UseShellExecute = false,
                    };

                    using (Process.Start(info))
                    {
                    }

                    _lastStartUtc = DateTime.UtcNow;
                    Log(LogType.Info, "Started the CNC HMI: " + ExePath);
                    return CncHmiLaunchResult.Started("The CNC HMI was started.");
                }
                catch (Exception ex)
                {
                    return Fail("The CNC HMI could not be started (" + ExePath + "): " + ex.Message, ex);
                }
            }
        }

        /// <summary>The CNC HMI already running from this exe, or null.</summary>
        private Process FindRunning()
        {
            Process[] candidates;
            try
            {
                candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExePath));
            }
            catch (Exception ex)
            {
                Log(LogType.Error, "Could not look for a running CNC HMI: " + ex.Message);
                return null;
            }

            Process match = null;
            foreach (Process candidate in candidates)
            {
                if (match == null && IsThisExe(candidate))
                    match = candidate;
                else
                    candidate.Dispose();
            }

            return match;
        }

        private bool IsThisExe(Process process)
        {
            try
            {
                if (process.HasExited)
                    return false;

                return string.Equals(process.MainModule.FileName, ExePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Win32Exception)
            {
                // Its module list cannot be read (an elevated process, say). Nothing else
                // carries this exe's name, so it is the CNC HMI.
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;   // exited while it was looked at
            }
        }

        private static bool BringToFront(Process process)
        {
            try
            {
                process.Refresh();
                IntPtr window = process.MainWindowHandle;
                if (window == IntPtr.Zero)
                    return false;

                if (IsIconic(window))
                    ShowWindow(window, SW_RESTORE);

                return SetForegroundWindow(window);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Bringing the CNC HMI forward failed: " + ex.Message);
                return false;
            }
        }

        private static CncHmiLaunchResult Fail(string message, Exception ex = null)
        {
            Log(LogType.Error, message, ex == null ? string.Empty : ex.ToString());
            return CncHmiLaunchResult.Failed(message);
        }

        private static void Log(LogType type, string message, string detail = "")
        {
            try
            {
                Logger.WriteLog(type, "CncHmi", "Launch", message, detail);
            }
            catch
            {
                // Logging never decides whether the CNC HMI starts.
            }
        }

        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr handle);
    }
}
