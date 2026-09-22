using System;
using System.Configuration;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Nova.Abstractions.Services;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// Windows implementation of <see cref="ILogFolderService"/>: path from
    /// App.config "LogFilePath" (same key as V3), opened via Windows Explorer.
    ///
    /// The Log nav entry is not a page — it opens this folder — so it can be pressed
    /// again and again, and every press used to launch another Explorer window. Two
    /// guards stop that: an already-open window for this folder is brought to the front
    /// instead of a new one being started, and presses inside a short window after a
    /// launch are ignored, which covers the gap before Explorer has created its window.
    /// </summary>
    public sealed class WindowsLogFolderService : ILogFolderService
    {
        /// <summary>How long after a launch further presses are ignored. Long enough
        /// for Explorer to create its window on a loaded machine, short enough that a
        /// deliberate second press is not swallowed.</summary>
        private static readonly TimeSpan LaunchCooldown = TimeSpan.FromSeconds(3);

        private readonly object _sync = new object();
        private DateTime _lastLaunchUtc = DateTime.MinValue;
        private string _lastLaunchPath;

        public string LogFolderPath =>
            ConfigurationManager.AppSettings["LogFilePath"] ?? string.Empty;

        public bool FolderExists => Directory.Exists(LogFolderPath);

        public void OpenLogFolder()
        {
            OpenFolder(LogFolderPath);
        }

        public bool OpenFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            lock (_sync)
            {
                // The cooldown is per folder, so Machine Log then HIVE Log in quick
                // succession still opens both.
                if (string.Equals(path, _lastLaunchPath, StringComparison.OrdinalIgnoreCase) &&
                    DateTime.UtcNow - _lastLaunchUtc < LaunchCooldown)
                {
                    // A window was just launched and may not have appeared yet; a second
                    // Process.Start here is what produced the stack of Explorer windows.
                    return true;
                }

                if (TryActivateExistingWindow(path))
                {
                    MarkLaunched(path);
                    return true;
                }

                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
                    MarkLaunched(path);
                }
                catch (Exception ex)
                {
                    // Never let a shell failure take out the nav bar.
                    System.Diagnostics.Debug.WriteLine("Opening folder " + path + " failed: " + ex.Message);
                }

                return true;
            }
        }

        private void MarkLaunched(string path)
        {
            _lastLaunchPath = path;
            _lastLaunchUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Finds an Explorer window already showing this folder and brings it forward.
        ///
        /// Explorer windows are top-level CabinetWClass/ExploreWClass windows titled with
        /// the folder name (or its full path, depending on the "display full path in the
        /// title bar" setting), so match on either. Process.Start would otherwise hand
        /// the request to the running explorer.exe, which opens a *new* window rather
        /// than reusing the one already on screen.
        /// </summary>
        private static bool TryActivateExistingWindow(string path)
        {
            try
            {
                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                string leaf = Path.GetFileName(full);
                IntPtr found = IntPtr.Zero;

                EnumWindows((handle, _) =>
                {
                    if (!IsWindowVisible(handle))
                        return true;

                    string className = GetText(handle, GetClassName);
                    if (className != "CabinetWClass" && className != "ExploreWClass")
                        return true;

                    string title = GetText(handle, GetWindowText);
                    if (string.Equals(title, leaf, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(title, full, StringComparison.OrdinalIgnoreCase))
                    {
                        found = handle;
                        return false;   // stop enumerating
                    }

                    return true;
                }, IntPtr.Zero);

                if (found == IntPtr.Zero)
                    return false;

                if (IsIconic(found))
                    ShowWindow(found, SW_RESTORE);

                SetForegroundWindow(found);
                return true;
            }
            catch (Exception ex)
            {
                // If the window hunt fails for any reason, fall back to launching one.
                System.Diagnostics.Debug.WriteLine("Explorer window lookup failed: " + ex.Message);
                return false;
            }
        }

        private static string GetText(IntPtr handle, Func<IntPtr, StringBuilder, int, int> read)
        {
            var buffer = new StringBuilder(512);
            int length = read(handle, buffer, buffer.Capacity);
            return length > 0 ? buffer.ToString() : string.Empty;
        }

        private const int SW_RESTORE = 9;

        private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr handle, StringBuilder buffer, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr handle, StringBuilder buffer, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr handle);
    }
}
