using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nova.Abstractions.Data.Models;
using Nova.Alarm;
using Nova.Hive;
using Nova.Wpf.App.Services.Twincat.Mapping;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// The results of the two audit procedures that are not finished units, taken from the
    /// PLC and uploaded to HIVE while their planned downtime is on:
    /// <list type="bullet">
    /// <item>PD-13 CCD Repeatability Testing (S3.4 V3.0): each capture the PLC counts
    /// (symbol map: Calibration.Repeatability). The first is kept; from the second on the
    /// spread so far, <c>{gantry}_delta_x_test{n}</c> and <c>_y_</c>, is uploaded and counts
    /// one towards the round.</item>
    /// <item>PD-14 Dispensing Pose Calibration (S3.5 V3.0): the results of one whole
    /// calibration (Calibration.Pose). Every result is checked against its Spec Limits row;
    /// one out of spec, or a machine alarm during the calibration, and nothing is uploaded
    /// and the OSS is told. All good: one upload, under the scanned test SN.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Both go through <see cref="HiveService.SendUnit"/>, which adds the audit keys, so the
    /// gateway's 1024 / 1025 answers count as success. With a section blank in the map its
    /// procedure only changes the HIVE state, and the startup warnings say so.
    /// </remarks>
    public sealed class AuditResultWatcher
    {
        private readonly TwincatPlcConnection _plc;
        private readonly HiveService _hive;
        private readonly HiveUnitDataBuilder _builder;
        private readonly RepeatabilitySymbols _repeatability;
        private readonly PoseCalibrationSymbols _pose;
        private readonly string _unitSerialSymbol;
        private readonly PlcEventCounter _captures;
        private readonly PlcEventCounter _calibrations;

        private RepeatabilityRun _run;
        private AuditSession _runSession;
        private int _runRound;

        public AuditResultWatcher(TwincatPlcConnection plc, PlcSubscriptions subscriptions, HiveService hive,
            HiveUnitDataBuilder builder, PlcSymbolMap map)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _hive = hive ?? throw new ArgumentNullException(nameof(hive));
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
            map = map ?? new PlcSymbolMap();
            _repeatability = map.Calibration.Repeatability ?? new RepeatabilitySymbols();
            _pose = map.Calibration.Pose ?? new PoseCalibrationSymbols();
            _unitSerialSymbol = map.Status.UnitDoneSerial;

            _captures = new PlcEventCounter(subscriptions,
                _repeatability.IsMapped ? _repeatability.CaptureCount : string.Empty, "CCD repeatability capture", OnCaptures);
            _calibrations = new PlcEventCounter(subscriptions,
                _pose.IsMapped ? _pose.DoneCount : string.Empty, "pose calibration", OnCalibrationDone);
        }

        /// <summary>Raised, on a worker thread, with a message the OSS has to see (a calibration that was not uploaded).</summary>
        public event EventHandler<string> Message;

        public bool RepeatabilityWired => _captures.IsWired;

        public bool PoseWired => _calibrations.IsWired;

        public void Start()
        {
            _captures.Start();
            _calibrations.Start();
        }

        // ------------------------------------------------------------------
        // PD-13
        // ------------------------------------------------------------------

        private AuditSession Active(string code)
        {
            AuditSession session = _hive.Audit;
            HiveError root = _hive.RootError;
            return session != null && !session.IsFinished && session.Code == code
                && _hive.CurrentState == HiveStateMachine.Downtime && root != null
                && string.Equals(root.Code, code, StringComparison.OrdinalIgnoreCase)
                ? session
                : null;
        }

        private void OnCaptures(int captures)
        {
            AuditSession session = Active(AuditSession.CcdRepeatabilityCode);
            if (session == null)
                return;

            if (captures > 1)
            {
                Log(LogType.Error, "[Error/Alert] The PLC counted " + captures + " CCD captures at once and holds only the last one: "
                    + (captures - 1) + " capture(s) are missing from the repeatability test.");
            }

            // A new round - continue, next operator - starts its own min and max.
            if (_run == null || !ReferenceEquals(_runSession, session) || _runRound != session.Round)
            {
                _run = new RepeatabilityRun();
                _runSession = session;
                _runRound = session.Round;
            }

            double? x, y, station;
            string serial;
            try
            {
                x = PlcValues.Number(_plc, _repeatability.X);
                y = PlcValues.Number(_plc, _repeatability.Y);
                station = PlcValues.Number(_plc, _repeatability.Station);
                serial = PlcValues.Text(_plc, _repeatability.Serial);
                if (string.IsNullOrWhiteSpace(serial))
                    serial = Convert.ToString(PlcValues.Read(_plc, _unitSerialSymbol), CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                Log(LogType.Error, "[Error/Alert] A CCD repeatability capture could not be read from the PLC: " + ex.Message);
                return;
            }

            if (!x.HasValue || !y.HasValue)
            {
                Log(LogType.Error, "[Error/Alert] A CCD repeatability capture had no X / Y result: it is not counted.");
                return;
            }

            string gantry = GantryOf(station);
            RepeatabilityDelta delta = _run.Add(gantry, x.Value, y.Value);
            if (delta == null)
            {
                Log(LogType.Info, "CCD repeatability, " + gantry + ": first capture recorded (" + Format(x.Value) + ", " + Format(y.Value) + "); nothing is uploaded for it.");
                return;
            }

            serial = (serial ?? string.Empty).Trim();
            if (serial.Length == 0)
            {
                Log(LogType.Error, "[Error/Alert] CCD repeatability capture " + delta.Captures + " has no serial number "
                    + "(Calibration.Repeatability.Serial or Status.UnitDoneSerial): its deltas were not uploaded.");
                return;
            }

            var values = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                { RepeatabilityRun.Key(gantry, "x", session.TestNumber), Math.Round(delta.DeltaX, 6) },
                { RepeatabilityRun.Key(gantry, "y", session.TestNumber), Math.Round(delta.DeltaY, 6) },
            };

            HiveUnitData unit = _builder.BuildAudit(serial, DateTime.Now, values, LimitKind(session));
            _hive.SendUnit(unit);
            _hive.UnitProcessed(serial);
        }

        /// <summary>"front" / "rear" by the station that made the capture; station 1 when the PLC does not say.</summary>
        private string GantryOf(double? station)
        {
            string number = ((int)Math.Round(station ?? 1)).ToString(CultureInfo.InvariantCulture);
            string gantry;
            return _repeatability.Gantries != null && _repeatability.Gantries.TryGetValue(number, out gantry) && !string.IsNullOrWhiteSpace(gantry)
                ? gantry.Trim()
                : "station" + number;
        }

        // ------------------------------------------------------------------
        // PD-14
        // ------------------------------------------------------------------

        private void OnCalibrationDone(int calibrations)
        {
            AuditSession session = Active(AuditSession.PoseCalibrationCode);
            if (session == null)
                return;

            // "If the machine is down during the calibration period, the calibration process needs to be restarted."
            if (session.FailedBecause != null)
            {
                Tell("The dispensing pose calibration was not uploaded: " + session.FailedBecause
                    + ". Clear the alarm, change the HIVE state to Running and start PD-14 again.");
                return;
            }

            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            var unread = new List<string>();
            foreach (KeyValuePair<string, ParameterSymbol> result in _pose.Results ?? new Dictionary<string, ParameterSymbol>())
            {
                if (result.Value == null || !PlcSymbolMap.Has(result.Value.Symbol))
                    continue;

                try
                {
                    double? value = PlcValues.Number(_plc, result.Value);
                    if (value.HasValue)
                        values[result.Key] = value.Value;
                    else
                        unread.Add(result.Key);
                }
                catch (Exception ex)
                {
                    unread.Add(result.Key + " (" + ex.Message + ")");
                }
            }

            if (unread.Count > 0 || values.Count == 0)
            {
                _hive.FailAudit("results could not be read from the PLC");
                Tell("The dispensing pose calibration was not uploaded: " + (values.Count == 0 ? "the PLC gave no result" : "these results could not be read - " + string.Join(", ", unread))
                    + ". Change the HIVE state to Running and start PD-14 again.");
                return;
            }

            // "Any calibration value out of specification terminates the calibration process.
            // No data is uploaded to HIVE, and a pop-up reminds the OSS to check and fine-tune."
            IDictionary<string, HiveLimit> limits = _builder.LimitsOf(values.Keys, LimitKind(session));
            var outOfSpec = new List<string>();
            var noLimit = new List<string>();
            foreach (KeyValuePair<string, double> value in values)
            {
                HiveLimit limit;
                if (!limits.TryGetValue(value.Key, out limit))
                {
                    noLimit.Add(value.Key);
                    continue;
                }

                if ((limit.Lower.HasValue && value.Value < limit.Lower.Value) || (limit.Upper.HasValue && value.Value > limit.Upper.Value))
                {
                    outOfSpec.Add(value.Key + " = " + Format(value.Value) + " (limits "
                        + (limit.Lower.HasValue ? Format(limit.Lower.Value) : "none") + " to "
                        + (limit.Upper.HasValue ? Format(limit.Upper.Value) : "none") + ")");
                }
            }

            if (noLimit.Count > 0)
            {
                Log(LogType.Error, "Pose calibration: no spec limit row in Config > Spec Limits for " + string.Join(", ", noLimit)
                    + " - not checked, sent without limits.");
            }

            if (outOfSpec.Count > 0)
            {
                _hive.FailAudit("a calibration value was out of specification");
                Tell("The dispensing pose calibration is out of specification, so nothing was uploaded to HIVE:"
                    + Environment.NewLine + string.Join(Environment.NewLine, outOfSpec)
                    + Environment.NewLine + "Check and fine-tune the machine, change the HIVE state to Running and start PD-14 again.");
                return;
            }

            string serial = (session.TestSn ?? string.Empty).Trim();
            if (serial.Length == 0)
            {
                Tell("The dispensing pose calibration was not uploaded: no test SN was scanned when PD-14 was started.");
                return;
            }

            HiveUnitData unit = _builder.BuildAudit(serial, DateTime.Now, values, LimitKind(session));
            _hive.SendUnit(unit);
            _hive.UnitProcessed(serial);
            Log(LogType.Info, "Pose calibration uploaded to HIVE under " + serial + ": " + string.Join(", ", values.Select(v => v.Key + " = " + Format(v.Value))));
        }

        /// <summary>The limits an audit is judged by: the CPK or GRR table when it has the row, else Production.</summary>
        private static AeLimitKind LimitKind(AuditSession session)
        {
            return session.Analysis == AuditSession.Grr ? AeLimitKind.Grr
                : session.Analysis == AuditSession.Cpk ? AeLimitKind.Cpk
                : AeLimitKind.Production;
        }

        private void Tell(string message)
        {
            Log(LogType.Error, "[Error/Alert] " + message);
            Message?.Invoke(this, message);
        }

        private static string Format(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static void Log(LogType type, string message)
        {
            Logger.WriteLog(type, "Hive", "Audit", message, string.Empty);
        }
    }
}
