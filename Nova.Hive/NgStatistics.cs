using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Hive
{
    /// <summary>
    /// "Machine down logic: 3x continuous or 5x discontinuous NG" (T6 V1, Option 1; DFM
    /// slide 32, error type P "Consecutive Error").
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>A unit's final NG counts one; the PLC's own retries do not reach the HMI.</item>
    /// <item>An NG below the limits: pop-up and buzzer, the machine goes on (<see cref="Warning"/>).</item>
    /// <item><see cref="ConsecutiveLimit"/> NG in a row, or <see cref="TotalLimit"/> NG in
    /// total, puts the machine down: <see cref="HiveService.ReportFault"/> with
    /// <see cref="ErrorCode"/>, which also asks the PLC to stop after the unit in process.</item>
    /// <item>A good unit ends the run of consecutive NG; it does not touch the total.</item>
    /// <item>Counted: every NG unit the PLC reports (MES, vision, scanning, laser, value out
    /// of spec, recheck, positioning) and a unit HIVE's own check failed. Not counted:
    /// communication failures - a unit HIVE did not confirm is a downtime at once
    /// (<see cref="HiveService.UploadFailedCode"/>).</item>
    /// <item>Nothing is counted while HIVE is in downtime or off (T6 rule 2.1: a failure
    /// while the machine is stopping "will be excluded from the statistical data"; debug
    /// and audit units are not production).</item>
    /// <item>Per gantry for single-glue dispensing, per machine for dual glue
    /// (<see cref="PerGantry"/>).</item>
    /// </list>
    /// The counts are kept in a small file so a restart does not clear them. They start again
    /// when HIVE leaves the downtime they raised - the operator has dealt with it. Until AME
    /// says otherwise (plan Q5) nothing else resets the total.
    /// </remarks>
    public sealed class NgStatistics : IDisposable
    {
        /// <summary>DFM slide 32: process error P, "Consecutive Error", on the dispensing station. Proposed; AME confirms.</summary>
        public const string ErrorCode = "P99DSOO-01";

        private const string MachineScope = "Machine";
        private const int SerialsKept = 5;

        private readonly HiveService _hive;
        private readonly string _stateFile;
        private readonly object _gate = new object();
        private Dictionary<string, Scope> _scopes;
        private bool _tripped;

        /// <param name="stateFile">Where the counts are kept; null keeps them in memory only.</param>
        public NgStatistics(HiveService hive, string stateFile, bool perGantry, int consecutiveLimit, int totalLimit)
        {
            _hive = hive ?? throw new ArgumentNullException(nameof(hive));
            _stateFile = stateFile;
            PerGantry = perGantry;
            ConsecutiveLimit = consecutiveLimit > 0 ? consecutiveLimit : 3;
            TotalLimit = totalLimit > 0 ? totalLimit : 5;

            State read = Read();
            _scopes = read.Scopes ?? new Dictionary<string, Scope>(StringComparer.OrdinalIgnoreCase);
            _tripped = read.Tripped;
        }

        /// <summary>True counts each gantry on its own (single glue); false counts the machine (dual glue).</summary>
        public bool PerGantry { get; }

        public int ConsecutiveLimit { get; }

        public int TotalLimit { get; }

        /// <summary>
        /// Raised, on a worker thread, for an NG that does not stop the machine, with what to
        /// tell the operator: pop-up and buzzer, production goes on.
        /// </summary>
        public event EventHandler<string> Warning;

        public void Start()
        {
            _hive.UnitDataResult += OnUnitDataResult;
            _hive.StateChanged += OnStateChanged;
        }

        public void Dispose()
        {
            _hive.UnitDataResult -= OnUnitDataResult;
            _hive.StateChanged -= OnStateChanged;
        }

        /// <summary>One finished unit, with its final result. Call from the unit hand-off.</summary>
        /// <param name="station">The gantry / station that dispensed it; null when the PLC does not say.</param>
        public void UnitFinished(string serial, bool pass, int? station, string failureCode)
        {
            if (!Counting)
                return;

            if (pass)
                Good(station);
            else
                Ng(serial, station, string.IsNullOrWhiteSpace(failureCode) ? "NG" : failureCode.Trim());
        }

        /// <summary>Production only: not in downtime (debug, audit, a machine already stopping) and not in manual.</summary>
        private bool Counting
        {
            get { return !_hive.IsSuspended && _hive.CurrentState != HiveStateMachine.Downtime; }
        }

        /// <summary>A unit HIVE's own check failed counts as "Upload HIVE Data Error" (T6 error list 8).</summary>
        private void OnUnitDataResult(object sender, HiveUnitResult result)
        {
            if (result == null || result.Outcome != HiveUnitOutcome.Refused || result.Unit == null || result.Unit.IsAudit)
                return;

            if (!Counting)
                return;

            int? station = null;
            object head;
            if (result.Unit.Data.TryGetValue("head_id", out head) && head != null)
            {
                try
                {
                    station = Convert.ToInt32(head, CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    station = null;
                }
            }

            Ng(result.Unit.UnitSn, station, "Upload HIVE Data Error");
        }

        private void Good(int? station)
        {
            lock (_gate)
            {
                Scope scope = ScopeOf(station);
                if (scope.Consecutive == 0)
                    return;

                scope.Consecutive = 0;
                Save();
            }
        }

        private void Ng(string serial, int? station, string why)
        {
            serial = (serial ?? string.Empty).Trim();
            string name;
            int consecutive, total;
            string units;
            bool trip;

            lock (_gate)
            {
                Scope scope = ScopeOf(station);
                name = ScopeName(station);

                // A unit the PLC already reported NG and HIVE then failed too is one NG, not two.
                if (serial.Length > 0 && string.Equals(scope.LastSerial, serial, StringComparison.OrdinalIgnoreCase))
                    return;

                scope.Consecutive++;
                scope.Total++;
                scope.LastSerial = serial;
                scope.Recent = (scope.Recent ?? new List<string>());
                scope.Recent.Add((serial.Length > 0 ? serial : "(no SN)") + " " + why);
                while (scope.Recent.Count > SerialsKept)
                    scope.Recent.RemoveAt(0);

                consecutive = scope.Consecutive;
                total = scope.Total;
                units = string.Join("; ", scope.Recent);
                trip = consecutive >= ConsecutiveLimit || total >= TotalLimit;
                if (trip)
                    _tripped = true;

                Save();
            }

            string where = PerGantry && name != MachineScope ? " on " + name : string.Empty;
            if (!trip)
            {
                string message = "Unit " + (serial.Length > 0 ? serial : "(no SN)") + " is NG" + where + ": " + why + ". "
                    + consecutive + " NG in a row (the machine stops at " + ConsecutiveLimit + "), "
                    + total + " NG in total (the machine stops at " + TotalLimit + "). The machine keeps running.";
                Logger.WriteLog(LogType.Error, "Hive", "NG", "[Error/Alert] " + message, "");
                Warning?.Invoke(this, message);
                return;
            }

            string reason = consecutive >= ConsecutiveLimit
                ? consecutive + " consecutive NG units"
                : total + " NG units in total";

            _hive.ReportFault(new HiveError
            {
                Code = ErrorCode,
                Message = "Consecutive Error",
                Detail = reason + where + ". Last units: " + units,
                Severity = "error",
                StopsMachine = true,
            });
        }

        /// <summary>Leaving the downtime the counts raised starts them again.</summary>
        private void OnStateChanged(object sender, int state)
        {
            if (state == HiveStateMachine.Downtime)
                return;

            lock (_gate)
            {
                if (!_tripped)
                    return;

                _tripped = false;
                _scopes = new Dictionary<string, Scope>(StringComparer.OrdinalIgnoreCase);
                Save();
            }

            Logger.WriteLog(LogType.Info, "Hive", "NG", "HIVE left the consecutive-error downtime: the NG counts start again.", "");
        }

        private string ScopeName(int? station)
        {
            return PerGantry && station.HasValue
                ? "Station " + station.Value.ToString(CultureInfo.InvariantCulture)
                : MachineScope;
        }

        private Scope ScopeOf(int? station)
        {
            string name = ScopeName(station);
            Scope scope;
            if (!_scopes.TryGetValue(name, out scope))
            {
                scope = new Scope();
                _scopes[name] = scope;
            }

            return scope;
        }

        private State Read()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_stateFile) && File.Exists(_stateFile))
                {
                    State read = JsonConvert.DeserializeObject<State>(File.ReadAllText(_stateFile));
                    if (read != null)
                    {
                        read.Scopes = new Dictionary<string, Scope>(
                            read.Scopes ?? new Dictionary<string, Scope>(), StringComparer.OrdinalIgnoreCase);
                        return read;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "NG", "The NG counts could not be read, so they start at 0: " + ex.Message, "");
            }

            return new State();
        }

        /// <summary>Called under the lock.</summary>
        private void Save()
        {
            if (string.IsNullOrWhiteSpace(_stateFile))
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_stateFile));
                var state = new State { Scopes = _scopes, Tripped = _tripped };
                File.WriteAllText(_stateFile, JsonConvert.SerializeObject(state, Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "NG", "The NG counts could not be saved: " + ex.Message, "");
            }
        }

        private sealed class State
        {
            public Dictionary<string, Scope> Scopes { get; set; }

            /// <summary>True while HIVE is in the downtime these counts raised.</summary>
            public bool Tripped { get; set; }
        }

        private sealed class Scope
        {
            public int Consecutive { get; set; }

            public int Total { get; set; }

            public string LastSerial { get; set; }

            public List<string> Recent { get; set; }
        }
    }
}
