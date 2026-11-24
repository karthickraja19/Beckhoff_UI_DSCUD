using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Nova.Abstractions.Services;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Hive
{
    /// <summary>
    /// <see cref="IHiveControl"/> over the running <see cref="HiveService"/>: the HIVE
    /// Control tab switches sending on and off, and the 24-hour dry run function module.
    /// </summary>
    /// <remarks>
    /// The on/off choice is kept in a file so it survives a restart; until the tab has
    /// been used, App.config's HiveEnabled decides. The dry run is deliberately not kept:
    /// S2.4 wants it "reset to Disabled after main software initialization".
    /// </remarks>
    public sealed class HiveControl : IHiveControl, IDisposable
    {
        private readonly HiveService _hive;
        private readonly HiveLinkStatus _link;
        private readonly string _stateFile;
        private readonly object _gate = new object();

        private string _disabledNote = string.Empty;
        private bool _dryRun;

        public HiveControl(HiveService hive, HiveLinkStatus link, string stateFile)
        {
            _hive = hive;
            _link = link;
            _stateFile = stateFile;

            // HiveService asks this while it builds each payload.
            _hive.Settings.DryRun = () => _dryRun;

            Stored stored = Read();
            if (stored != null && stored.HiveEnabled.HasValue)
            {
                _hive.Settings.Enabled = stored.HiveEnabled.Value;
                _disabledNote = stored.HiveEnabled.Value ? string.Empty : stored.Note ?? string.Empty;
            }
            else if (!_hive.Settings.Enabled)
            {
                _disabledNote = "Switched off in App.config (HiveEnabled).";
            }

            _hive.StateChanged += OnHiveStateChanged;
        }

        public event EventHandler Changed;

        public bool IsAvailable
        {
            get { return true; }
        }

        public bool IsEnabled
        {
            get { return _hive.Settings.Enabled; }
        }

        public string DisabledNote
        {
            get
            {
                lock (_gate)
                {
                    return IsEnabled ? string.Empty : _disabledNote;
                }
            }
        }

        public bool IsDryRunEnabled
        {
            get { return _dryRun; }
        }

        public int CurrentState
        {
            get { return _hive.CurrentState; }
        }

        public string CurrentDowntimeCode
        {
            get
            {
                HiveError root = _hive.RootError;
                return _hive.CurrentState == HiveStateMachine.Downtime && root != null ? root.Code : null;
            }
        }

        public bool SetEnabled(bool enabled, string reason, string operatorName, string approverName, out string error)
        {
            error = null;
            if (enabled == IsEnabled)
                return true;

            if (!enabled)
            {
                // S2.4 page 5: prior approval, and only for a network fault or an incomplete setup.
                if (string.IsNullOrWhiteSpace(reason))
                {
                    error = "Say why HIVE is being disabled.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(approverName))
                {
                    error = "Disabling HIVE needs a second approval.";
                    return false;
                }
            }

            string note = enabled
                ? string.Empty
                : "Disabled " + DateTime.Now.ToString("yyyy/MM/dd HH:mm") + " by " + operatorName
                  + ", approved by " + approverName + ": " + reason.Trim();

            if (!Write(new Stored { HiveEnabled = enabled, Note = note }, out error))
                return false;

            lock (_gate)
            {
                _disabledNote = note;
            }

            _hive.Settings.Enabled = enabled;

            // Slide 96: if HIVE is disabled the machine log must say HIVE is disconnected.
            Logger.WriteLog(LogType.Info, "Hive", "Control",
                enabled
                    ? "HIVE enabled by " + operatorName + " on the HIVE Control tab."
                    : "[Error/Alert] HIVE disconnected - " + note,
                "");

            if (_link != null)
                _link.Refresh();

            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public string DryRunUnavailableReason
        {
            get { return DryRunIds.UnavailableReason(_hive.Settings.DryRunSiCode); }
        }

        public void SetDryRun(bool enabled, string operatorName)
        {
            if (_dryRun == enabled)
                return;

            // Without the SI code and both DLLs the payloads could not carry what the dry run is for.
            string unavailable = enabled ? DryRunUnavailableReason : null;
            if (unavailable != null)
            {
                Logger.WriteLog(LogType.Error, "Hive", "Control", "The 24-hour dry run was not enabled: " + unavailable, "");
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }

            _dryRun = enabled;
            Logger.WriteLog(LogType.Info, "Hive", "Control",
                "24-hour dry run " + (enabled ? "enabled" : "disabled") + " by " + operatorName + " on the HIVE Control tab.", "");
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _hive.StateChanged -= OnHiveStateChanged;
        }

        private void OnHiveStateChanged(object sender, int state)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private Stored Read()
        {
            try
            {
                return File.Exists(_stateFile) ? JsonConvert.DeserializeObject<Stored>(File.ReadAllText(_stateFile)) : null;
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "Control", "Could not read " + _stateFile + ": " + ex.Message, "");
                return null;
            }
        }

        private bool Write(Stored stored, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_stateFile));
                File.WriteAllText(_stateFile, JsonConvert.SerializeObject(stored, Formatting.Indented), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                error = "The HIVE setting could not be saved: " + ex.Message;
                return false;
            }
        }

        private sealed class Stored
        {
            /// <summary>Null until the tab has been used; App.config decides until then.</summary>
            public bool? HiveEnabled { get; set; }

            public string Note { get; set; }
        }
    }
}
