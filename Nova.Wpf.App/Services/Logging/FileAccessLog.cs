using System;
using System.IO;
using System.Text;
using Nova.Abstractions.Services;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// Writes access events to <c>Access_yyyyMMdd.log</c> in the machine log folder -
    /// Audio SW DFM v5.4 slide 44, "All L1/L2/L3 actions are recorded in machine log".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tab-separated, one line per event, newest appended. Plain text on purpose: the
    /// people who read this file are on a shop floor with Notepad, and an audit trail
    /// that needs a tool to open is one that does not get checked.
    /// </para>
    /// <para>
    /// Every write is guarded. An audit log that cannot be written must not stop an
    /// operator signing in to a machine that is holding up a line - the failure goes
    /// to Debug and the login proceeds. That is a deliberate trade: availability over
    /// completeness of the trail. If the trail has to be authoritative, this is the
    /// class that changes, and a failed write would have to refuse the login.
    /// </para>
    /// </remarks>
    public sealed class FileAccessLog : IAccessLog
    {
        private readonly ILogFolderService _logFolders;
        private readonly object _sync = new object();

        public FileAccessLog(ILogFolderService logFolders)
        {
            _logFolders = logFolders;
        }

        public void LoginAttempt(string cardUid, bool granted, string name, int level, string detail)
        {
            Write(granted ? "LOGIN-GRANTED" : "LOGIN-REFUSED",
                name,
                level,
                "card=" + Mask(cardUid) + (string.IsNullOrWhiteSpace(detail) ? string.Empty : "; " + detail));
        }

        public void VerificationCompleted(string operatorName, string verifierName, string verifierCardUid)
        {
            Write("L3-VERIFIED",
                operatorName,
                3,
                "countersigned by " + verifierName + " (card=" + Mask(verifierCardUid) + ")");
        }

        public void Logout(string name, int level, string reason)
        {
            Write("LOGOUT", name, level, reason);
        }

        public void Action(string name, int level, string action)
        {
            Write("ACTION", name, level, action);
        }

        /// <summary>
        /// Shows only the last four characters of a badge UID. The log has to identify
        /// the person, which the name already does; reproducing every badge number in
        /// a plain-text file on a shared machine would turn the audit trail into a way
        /// of cloning badges.
        /// </summary>
        private static string Mask(string cardUid)
        {
            if (string.IsNullOrWhiteSpace(cardUid))
                return "(none)";

            string trimmed = cardUid.Trim();
            return trimmed.Length <= 4
                ? new string('*', trimmed.Length)
                : new string('*', trimmed.Length - 4) + trimmed.Substring(trimmed.Length - 4);
        }

        private void Write(string kind, string name, int level, string detail)
        {
            try
            {
                string folder = _logFolders == null ? null : _logFolders.LogFolderPath;
                if (string.IsNullOrWhiteSpace(folder))
                    return;

                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                string path = Path.Combine(folder, "Access_" + DateTime.Now.ToString("yyyyMMdd") + ".log");

                var line = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append('\t')
                    .Append(kind).Append('\t')
                    .Append(string.IsNullOrWhiteSpace(name) ? "-" : name).Append('\t')
                    .Append(level > 0 ? "L" + level : "-").Append('\t')
                    .Append(detail ?? string.Empty)
                    .AppendLine();

                // Serialised: the idle timeout can log a logout from its own timer
                // thread while the UI thread is logging an action.
                lock (_sync)
                {
                    File.AppendAllText(path, line.ToString());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Access log could not be written: " + ex.Message);
            }
        }
    }
}
