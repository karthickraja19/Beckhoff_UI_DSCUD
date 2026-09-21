namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Access to the machine's running-log folder (V3: App.config "LogFilePath",
    /// e.g. C:\TEAL_Log_Data). The Log nav entry is not a page — it opens this
    /// folder in the OS file browser.
    /// </summary>
    public interface ILogFolderService
    {
        /// <summary>Configured log-folder path; empty when not configured.</summary>
        string LogFolderPath { get; }

        /// <summary>True when <see cref="LogFolderPath"/> exists on disk.</summary>
        bool FolderExists { get; }

        /// <summary>Opens <see cref="LogFolderPath"/> in the OS file browser.</summary>
        void OpenLogFolder();
    }
}
