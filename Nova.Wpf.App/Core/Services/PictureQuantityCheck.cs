using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Core.Services
{
    /// <summary>
    /// <see cref="IPictureQuantityCheck"/> - Audio Picture Upload Quantity Check (T5, X7).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Each unit is written to Pic_Upload_Record (one CSV per day): time, unit_sn,
    /// pic_quantity, LSL, USL and Match_Result (LSL &lt;= quantity &lt;= USL).</item>
    /// <item>The limits are the Pic_Upload_quantity row of Config &gt; Spec Limits (Production).</item>
    /// <item>A unit outside them sets "dispensing control" to 0 - the SCUD may not
    /// dispense - and puts the machine down with "SN:... Picture quantity X pcs does not
    /// match the target."</item>
    /// <item>Only a two-step verified user sets it back to 1, and a HIVE state change never does.</item>
    /// </list>
    /// </remarks>
    public sealed class PictureQuantityCheck : IPictureQuantityCheck
    {
        /// <summary>The Spec Limits row the picture count is checked against.</summary>
        public const string LimitParameter = "Pic_Upload_quantity";

        private const int KeepInMemory = 200;

        private readonly IMachineSettingsService _settings;
        private readonly Action<string> _raiseDowntime;
        private readonly Action<bool> _writeToPlc;
        private readonly string _stateFile;
        private readonly object _gate = new object();
        private readonly List<PictureUploadRecord> _recent = new List<PictureUploadRecord>();

        private State _state;

        /// <param name="raiseDowntime">Puts the machine down with this error detail; null without HIVE.</param>
        /// <param name="writeToPlc">Passes the dispensing control value to the PLC; null when there is no symbol.</param>
        public PictureQuantityCheck(
            string dataDirectory,
            string recordFolder,
            IMachineSettingsService settings,
            Action<string> raiseDowntime,
            Action<bool> writeToPlc)
        {
            _settings = settings;
            _raiseDowntime = raiseDowntime;
            _writeToPlc = writeToPlc;
            _stateFile = Path.Combine(dataDirectory ?? string.Empty, "Settings", "PictureQuantityCheck.json");
            RecordFolder = string.IsNullOrWhiteSpace(recordFolder) ? @"D:\Pic_Upload_Record" : recordFolder.Trim();

            _state = ReadState();
            LoadToday();
            Push(_state.DispensingEnabled);
        }

        public event EventHandler Changed;

        public bool DispensingEnabled
        {
            get
            {
                lock (_gate)
                {
                    return _state.DispensingEnabled;
                }
            }
        }

        public string LastChange
        {
            get
            {
                lock (_gate)
                {
                    return _state.LastChange ?? string.Empty;
                }
            }
        }

        public bool IsCheckEnabled
        {
            get
            {
                lock (_gate)
                {
                    return !_state.CheckDisabled;
                }
            }
        }

        public bool SetCheckEnabled(bool enabled, string operatorName, out string error)
        {
            State next;
            lock (_gate)
            {
                next = new State
                {
                    DispensingEnabled = _state.DispensingEnabled,
                    LastChange = _state.LastChange,
                    CheckDisabled = !enabled,
                };
            }

            if (!WriteState(next, out error))
                return false;

            lock (_gate)
            {
                _state = next;
            }

            Logger.WriteLog(LogType.Info, "Vision", "PictureCheck",
                "Picture quantity check " + (enabled ? "enabled" : "disabled") + " by " + operatorName + ".", "");
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public string RecordFolder { get; }

        public string LimitsText
        {
            get
            {
                double? lsl, usl;
                ReadLimits(out lsl, out usl);
                if (lsl == null && usl == null)
                    return "No " + LimitParameter + " limit in Config > Spec Limits (Production), so no unit is judged.";

                return (lsl.HasValue ? Format(lsl.Value) : "no minimum") + " to "
                    + (usl.HasValue ? Format(usl.Value) : "no maximum") + " pictures per unit";
            }
        }

        public bool EnableDispensing(string operatorName, string approverName, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(approverName))
            {
                error = "Setting dispensing control back to 1 needs two-step verification.";
                return false;
            }

            var next = new State
            {
                CheckDisabled = !IsCheckEnabled,
                DispensingEnabled = true,
                LastChange = "Set to 1 on " + DateTime.Now.ToString("yyyy/MM/dd HH:mm") + " by " + operatorName + ", approved by " + approverName,
            };

            if (!WriteState(next, out error))
                return false;

            lock (_gate)
            {
                _state = next;
            }

            Logger.WriteLog(LogType.Info, "Vision", "PictureCheck", "Dispensing control set to 1 by " + operatorName + ", approved by " + approverName + ".", "");
            Push(true);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public PictureUploadRecord ReportUnit(string unitSn, int pictureQuantity)
        {
            double? lsl, usl;
            ReadLimits(out lsl, out usl);

            var record = new PictureUploadRecord
            {
                Time = DateTime.Now,
                UnitSn = unitSn ?? string.Empty,
                Quantity = pictureQuantity,
                Lsl = lsl,
                Usl = usl,
                Match = (!lsl.HasValue || pictureQuantity >= lsl.Value) && (!usl.HasValue || pictureQuantity <= usl.Value),
            };

            AppendRecord(record);

            lock (_gate)
            {
                _recent.Insert(0, record);
                if (_recent.Count > KeepInMemory)
                    _recent.RemoveAt(_recent.Count - 1);
            }

            if (!record.Match && !IsCheckEnabled)
            {
                Logger.WriteLog(LogType.Error, "Vision", "PictureCheck", "[Error/Alert] SN:" + record.UnitSn + ". Picture quantity " + pictureQuantity
                    + " pcs does not match the target. The picture quantity check is disabled, so the machine goes on.", "");
            }
            else if (!record.Match)
            {
                string detail = "SN:" + record.UnitSn + ". Picture quantity " + pictureQuantity + " pcs does not match the target.";
                var next = new State
                {
                    CheckDisabled = false,
                    DispensingEnabled = false,
                    LastChange = "Set to 0 on " + record.Time.ToString("yyyy/MM/dd HH:mm:ss") + ": " + detail,
                };

                string ignored;
                WriteState(next, out ignored);
                lock (_gate)
                {
                    _state = next;
                }

                Logger.WriteLog(LogType.Error, "Vision", "PictureCheck", "[Error/Alert] " + detail, "");
                Push(false);
                if (_raiseDowntime != null)
                    _raiseDowntime(detail);
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return record;
        }

        public IReadOnlyList<PictureUploadRecord> Recent(int max)
        {
            lock (_gate)
            {
                return _recent.Take(Math.Max(0, max)).ToList();
            }
        }

        private void ReadLimits(out double? lsl, out double? usl)
        {
            lsl = null;
            usl = null;
            if (_settings == null)
                return;

            try
            {
                AeLimitRecord row = (_settings.LoadAeLimits(AeLimitKind.Production) ?? new List<AeLimitRecord>())
                    .FirstOrDefault(r => string.Equals((r.Parameter ?? string.Empty).Trim(), LimitParameter, StringComparison.OrdinalIgnoreCase));
                if (row == null)
                    return;

                double value;
                if (double.TryParse(row.Min, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    lsl = value;
                if (double.TryParse(row.Max, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    usl = value;
            }
            catch (Exception)
            {
                // Limits that cannot be read judge nothing; the tab says so.
            }
        }

        private void AppendRecord(PictureUploadRecord record)
        {
            try
            {
                Directory.CreateDirectory(RecordFolder);
                string file = Path.Combine(RecordFolder, "Pic_Upload_Record_" + record.Time.ToString("yyyyMMdd") + ".csv");
                bool header = !File.Exists(file);
                var line = new StringBuilder();
                if (header)
                    line.AppendLine("Time,unit_sn,pic_quantity,LSL,USL,Match_Result");

                line.AppendLine(string.Join(",",
                    record.UnitSn + "_" + record.Time.ToString("yyyyMMddHHmmss"),
                    record.UnitSn,
                    record.Quantity.ToString(CultureInfo.InvariantCulture),
                    record.Lsl.HasValue ? Format(record.Lsl.Value) : "",
                    record.Usl.HasValue ? Format(record.Usl.Value) : "",
                    record.Match ? "Y" : "N"));

                File.AppendAllText(file, line.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Vision", "PictureCheck", "Could not write Pic_Upload_Record: " + ex.Message, "");
            }
        }

        private void LoadToday()
        {
            try
            {
                string file = Path.Combine(RecordFolder, "Pic_Upload_Record_" + DateTime.Now.ToString("yyyyMMdd") + ".csv");
                if (!File.Exists(file))
                    return;

                foreach (string line in File.ReadAllLines(file).Skip(1).Reverse().Take(KeepInMemory))
                {
                    string[] cells = line.Split(',');
                    if (cells.Length < 6)
                        continue;

                    int quantity;
                    int.TryParse(cells[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity);
                    string stamp = cells[0].Length >= 14 ? cells[0].Substring(cells[0].Length - 14) : string.Empty;
                    DateTime time;
                    DateTime.TryParseExact(stamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

                    _recent.Add(new PictureUploadRecord
                    {
                        Time = time,
                        UnitSn = cells[1],
                        Quantity = quantity,
                        Lsl = ParseNullable(cells[3]),
                        Usl = ParseNullable(cells[4]),
                        Match = cells[5].Trim() == "Y",
                    });
                }
            }
            catch (Exception)
            {
                // Today's history is a convenience for the tab; the file itself is untouched.
            }
        }

        private void Push(bool enabled)
        {
            if (_writeToPlc == null)
                return;

            try
            {
                _writeToPlc(enabled);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Vision", "PictureCheck", "Could not pass dispensing control to the PLC: " + ex.Message, ex.ToString());
            }
        }

        private State ReadState()
        {
            try
            {
                if (File.Exists(_stateFile))
                {
                    State read = JsonConvert.DeserializeObject<State>(File.ReadAllText(_stateFile));
                    if (read != null)
                        return read;
                }
            }
            catch (Exception)
            {
                // Falls through to the default: dispensing allowed.
            }

            return new State { DispensingEnabled = true };
        }

        private bool WriteState(State state, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_stateFile));
                File.WriteAllText(_stateFile, JsonConvert.SerializeObject(state, Formatting.Indented), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                error = "The picture check setting could not be saved: " + ex.Message;
                return false;
            }
        }

        private static double? ParseNullable(string text)
        {
            double value;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : (double?)null;
        }

        private static string Format(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private sealed class State
        {
            public bool DispensingEnabled { get; set; }

            public string LastChange { get; set; }

            /// <summary>True while a Level 3 user has switched the check off. Off is the exception, so absent means on.</summary>
            public bool CheckDisabled { get; set; }
        }
    }
}
