using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;
using Nova.Hive;
using Nova.Wpf.App.Services.Twincat.Mapping;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Collects what HIVE is told about one finished unit - its machine_data payload
    /// (Audio SW DFM v5.4 S1.2, slides 15 to 19).
    /// </summary>
    /// <remarks>
    /// Where each key comes from:
    /// <list type="bullet">
    /// <item>unit_sn, pass, unit_ct, failure_code, fixture_id, head_id, first_vision_tossing_NG:
    /// the PLC's unit record (symbol map: Status.UnitDoneSerial, UnitRecord).</item>
    /// <item>input_time, output_time, output_ct: worked out here. output_time is when the PLC
    /// counted the unit, input_time is that less the cycle time, output_ct is the time since
    /// the unit before (0 for the first, at most 3600 - S1.2.3).</item>
    /// <item>every process parameter (temperatures, pressures, counts, ...): the symbol map's
    /// Parameters, by the key HIVE wants - the same list Home's Parameter Dashboard shows. A
    /// new value is its PLC variable and a line in the map, no code change.</item>
    /// <item>limits: the Config &gt; Spec Limits (Production) row of the same name.</item>
    /// <item>glue_sn, valve_sn, nozzle_sn, valve_manufacturing_date: the consumables the
    /// planned-downtime procedures recorded, per gantry ("front_" / "rear_" on a D-SCUD).</item>
    /// <item>w, b, R2, last_glue_weight, last_weigh_time, purge_idx: the glue figures behind
    /// Data &gt; Glue, when the PLC gives any.</item>
    /// <item>station_id, mac_addr, machine_type, sw_version, ver_dig, limits_version, sequence:
    /// this machine's own settings.</item>
    /// </list>
    /// A key whose source is not there is left out of the payload - never filled in with a
    /// stand-in value - and HIVE's own check then names it as missing.
    /// </remarks>
    public sealed class HiveUnitDataBuilder
    {
        private const double MaxOutputCt = 3600;

        private readonly TwincatPlcConnection _plc;
        private readonly UnitRecordSymbols _unit;
        private readonly Dictionary<string, ParameterSymbol> _parameters;
        private readonly HiveSettings _hive;
        private readonly IMachineSettingsService _limits;
        private readonly IConsumableStore _consumables;
        private readonly IDispenserDataService _dispenser;
        private readonly string _stationId;
        private readonly string _limitsVersion;
        private readonly string _macAddress;

        private DateTime? _lastOutputTime;

        /// <param name="dispenser">The glue figures; null when the PLC gives none.</param>
        /// <param name="stationId">The Insight station id (App.config TerminalName).</param>
        /// <param name="limitsVersion">The revision of the spec limits (App.config HiveLimitsVersion).</param>
        public HiveUnitDataBuilder(
            TwincatPlcConnection plc, PlcSymbolMap map, HiveSettings hive, IMachineSettingsService limits,
            IConsumableStore consumables, IDispenserDataService dispenser, string stationId, string limitsVersion)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _hive = hive ?? throw new ArgumentNullException(nameof(hive));
            map = map ?? new PlcSymbolMap();
            _unit = map.UnitRecord;
            _parameters = map.Parameters
                .Where(p => p.Value != null && PlcSymbolMap.Has(p.Value.Symbol))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            _limits = limits;
            _consumables = consumables;
            _dispenser = dispenser;
            _stationId = (stationId ?? string.Empty).Trim();
            _limitsVersion = (limitsVersion ?? string.Empty).Trim();
            _macAddress = ReadMacAddress();

            var missing = new List<string>();
            if (!PlcSymbolMap.Has(map.Status.UnitDoneCount)) missing.Add("Status.UnitDoneCount");
            if (!PlcSymbolMap.Has(map.Status.UnitDoneSerial)) missing.Add("Status.UnitDoneSerial");
            if (!PlcSymbolMap.Has(_unit.Pass?.Symbol)) missing.Add("UnitRecord.Pass");
            if (!PlcSymbolMap.Has(_unit.CycleTime?.Symbol)) missing.Add("UnitRecord.CycleTime");

            if (missing.Count > 0)
            {
                UnavailableReason = "No unit data (machine_data) is sent to HIVE: " + string.Join(", ", missing)
                    + (missing.Count == 1 ? " is" : " are") + " blank in Data\\" + PlcSymbolMap.FileName
                    + ". A unit needs its serial number, its result and its cycle time.";
            }
        }

        /// <summary>Why no unit can be reported, or null when the PLC gives what a unit needs.</summary>
        public string UnavailableReason { get; }

        /// <summary>Keys the payload needs from this machine's settings that are not set, for the startup warnings.</summary>
        public IEnumerable<string> SettingWarnings()
        {
            if (_stationId.Length == 0)
                yield return "HIVE unit data goes out without station_id: TerminalName is blank in App.config.";
            if (_limitsVersion.Length == 0)
                yield return "HIVE unit data goes out without limits_version: HiveLimitsVersion is blank in App.config.";
            if (string.IsNullOrWhiteSpace(_hive.SoftwareVersion) || !VersionDigits(_hive.SoftwareVersion).HasValue)
                yield return "HIVE unit data goes out without ver_dig: the software version is not in the AAA_X.X.X.X_YYMMDD_TYPE format.";
            if (_macAddress == null)
                yield return "HIVE unit data goes out without mac_addr: no network adapter with a MAC address was found.";
        }

        /// <summary>
        /// The finished unit the PLC is holding now. Null when it cannot be reported - no
        /// serial number, no result or no cycle time - which is written to the log.
        /// Call in the order the units finish: output_ct is the time since the last call.
        /// </summary>
        public HiveUnitData Build(string serial, DateTime outputTime)
        {
            if (UnavailableReason != null)
                return null;

            if (string.IsNullOrWhiteSpace(serial))
            {
                Skip("the PLC gave no serial number for it");
                return null;
            }

            bool? pass;
            double? cycle;
            try
            {
                pass = PlcValues.Flag(_plc, _unit.Pass);
                cycle = PlcValues.Number(_plc, _unit.CycleTime);
            }
            catch (Exception ex)
            {
                Skip("unit " + serial + ": its result or cycle time could not be read (" + ex.Message + ")");
                return null;
            }

            if (!pass.HasValue || !cycle.HasValue || cycle.Value < 0)
            {
                Skip("unit " + serial + ": the PLC gave no result or no cycle time");
                return null;
            }

            var unit = new HiveUnitData
            {
                UnitSn = serial.Trim(),
                Pass = pass.Value,
                OutputTime = outputTime,
                InputTime = outputTime.AddSeconds(-cycle.Value),
            };

            // S1.2.3: unit_ct = output_time - input_time; output_ct = this output_time - the
            // previous unit's, 0 for the first unit, at most 3600.
            unit.Data["unit_ct"] = Math.Round(cycle.Value, 2);
            unit.Data["output_ct"] = _lastOutputTime.HasValue
                ? Math.Round(Math.Min(MaxOutputCt, Math.Max(0, (outputTime - _lastOutputTime.Value).TotalSeconds)), 2)
                : 0d;
            _lastOutputTime = outputTime;

            AddMachineKeys(unit);
            AddUnitRecord(unit);
            AddParameters(unit);
            AddConsumables(unit);
            AddGlueFigures(unit);
            AddLimits(unit);
            AddAoiStates(unit);

            return unit;
        }

        /// <summary>
        /// The payload of one audit result that is not a finished unit: a CCD repeatability
        /// delta (S3.4) or a pose calibration (S3.5). It carries this machine's own keys, the
        /// given values, and the limits of the Spec Limits rows of the same names -
        /// <paramref name="limitKind"/> first, then Production. HIVE adds hive_state and the
        /// audit keys when it is sent.
        /// </summary>
        public HiveUnitData BuildAudit(string serial, DateTime time, IDictionary<string, double> values, AeLimitKind limitKind)
        {
            var unit = new HiveUnitData
            {
                UnitSn = (serial ?? string.Empty).Trim(),
                Pass = true,
                InputTime = time,
                OutputTime = time,
            };

            AddMachineKeys(unit);
            foreach (KeyValuePair<string, double> value in values ?? new Dictionary<string, double>())
                unit.Data[value.Key] = value.Value;

            foreach (KeyValuePair<string, HiveLimit> limit in LimitsOf(values != null ? values.Keys : new string[0], limitKind))
                unit.Limits[limit.Key] = limit.Value;

            return unit;
        }

        /// <summary>The spec limits of these keys: the row of the same name in <paramref name="kind"/>, else in Production.</summary>
        public IDictionary<string, HiveLimit> LimitsOf(IEnumerable<string> keys, AeLimitKind kind)
        {
            var found = new Dictionary<string, HiveLimit>(StringComparer.Ordinal);
            if (_limits == null || keys == null)
                return found;

            var wanted = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            foreach (AeLimitKind source in kind == AeLimitKind.Production
                ? new[] { AeLimitKind.Production }
                : new[] { kind, AeLimitKind.Production })
            {
                try
                {
                    foreach (AeLimitRecord row in _limits.LoadAeLimits(source) ?? new List<AeLimitRecord>())
                    {
                        string key = (row.Parameter ?? string.Empty).Trim();
                        if (!wanted.Contains(key))
                            continue;

                        string name = wanted.First(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                        if (found.ContainsKey(name))
                            continue;

                        double number;
                        var limit = new HiveLimit();
                        if (double.TryParse(row.Min, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                            limit.Lower = number;
                        if (double.TryParse(row.Max, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                            limit.Upper = number;

                        if (limit.Lower.HasValue || limit.Upper.HasValue)
                            found[name] = limit;
                    }
                }
                catch (Exception ex)
                {
                    Log("The " + source + " spec limits could not be read: " + ex.Message);
                }
            }

            return found;
        }

        /// <summary>S16 rule 1: the limits of a pose the vision software gave no code for.</summary>
        private const double AoiNoCodeLimit = 1111111;

        /// <summary>
        /// S16 Glue Path AOI Inspection State Tracking X4: one key per dispensing pose. A code
        /// with leading zeros loses them by being a number (0111222 goes out as 111222, rule 2);
        /// its limits are its Spec Limits row, added by <see cref="AddLimits"/>. A pose whose
        /// code is missing goes out as 0 with both limits fixed at 1111111 (rule 1).
        /// </summary>
        private void AddAoiStates(HiveUnitData unit)
        {
            foreach (KeyValuePair<string, ParameterSymbol> pose in _unit.AoiStates ?? new Dictionary<string, ParameterSymbol>())
            {
                if (string.IsNullOrWhiteSpace(pose.Key) || pose.Value == null || !PlcSymbolMap.Has(pose.Value.Symbol))
                    continue;

                double? code = Try(() => PlcValues.Number(_plc, pose.Value), pose.Key);
                if (code.HasValue && code.Value > 0)
                {
                    unit.Data[pose.Key] = (long)Math.Round(code.Value);
                    if (!unit.Limits.ContainsKey(pose.Key))
                        Log("Unit " + unit.UnitSn + ": " + pose.Key + " goes out without limits - it has no row in Config > Spec Limits (Production).");
                    continue;
                }

                unit.Data[pose.Key] = 0L;
                unit.Limits[pose.Key] = new HiveLimit { Lower = AoiNoCodeLimit, Upper = AoiNoCodeLimit };
                Log("Unit " + unit.UnitSn + ": no glue path AOI state code for " + pose.Key + " - sent as 0 with limits 1111111.");
            }
        }

        /// <summary>The keys that describe this machine rather than the unit (slide 15).</summary>
        private void AddMachineKeys(HiveUnitData unit)
        {
            unit.Data["sequence"] = _hive.Sequence;

            if (!string.IsNullOrWhiteSpace(_hive.SoftwareVersion))
            {
                unit.Data["sw_version"] = _hive.SoftwareVersion;

                double? digits = VersionDigits(_hive.SoftwareVersion);
                if (digits.HasValue)
                    unit.Data["ver_dig"] = digits.Value;
            }

            if (_limitsVersion.Length > 0)
                unit.Data["limits_version"] = _limitsVersion;

            if (_stationId.Length > 0)
                unit.Serials["station_id"] = _stationId;
            if (_macAddress != null)
                unit.Serials["mac_addr"] = _macAddress;
            if (!string.IsNullOrWhiteSpace(_hive.MachineType))
                unit.Serials["machine_type"] = _hive.MachineType;
        }

        /// <summary>What the PLC holds for the unit besides its result and cycle time.</summary>
        private void AddUnitRecord(HiveUnitData unit)
        {
            // Slide 15: "Every unit must have an entry in this field ... If passing, report NA".
            string failure = Try(() => PlcValues.Text(_plc, _unit.FailureCode), "failure code");
            if (unit.Pass)
                unit.Serials["failure_code"] = "NA";
            else if (!string.IsNullOrWhiteSpace(failure) && !string.Equals(failure, "NA", StringComparison.OrdinalIgnoreCase))
                unit.Serials["failure_code"] = failure;
            else
                Log("Unit " + unit.UnitSn + " failed but the PLC gave no failure code (UnitRecord.FailureCode): failure_code is not sent.");

            string fixture = Try(() => PlcValues.Text(_plc, _unit.FixtureId), "fixture id");
            if (!string.IsNullOrWhiteSpace(fixture))
                unit.Data["fixture_id"] = fixture;

            double? station = Try(() => PlcValues.Number(_plc, _unit.Station), "station");
            if (station.HasValue)
                unit.Data["head_id"] = (int)Math.Round(station.Value);

            // Slide 18: 1 when the first pre-process image capture was NG, 0 when OK.
            bool? tossed = Try(() => PlcValues.Flag(_plc, _unit.Tossing), "tossing");
            if (tossed.HasValue)
                unit.Data["first_vision_tossing_NG"] = tossed.Value ? 1 : 0;
        }

        /// <summary>Every process value the PLC publishes, under the key the symbol map gives it.</summary>
        private void AddParameters(HiveUnitData unit)
        {
            foreach (KeyValuePair<string, ParameterSymbol> parameter in _parameters)
            {
                double? value = Try(() => PlcValues.Number(_plc, parameter.Value), parameter.Key);
                if (value.HasValue)
                    unit.Data[parameter.Key] = value.Value;
            }
        }

        /// <summary>The glue, valve and nozzle each gantry is fitted with (slides 16 and 18).</summary>
        private void AddConsumables(HiveUnitData unit)
        {
            if (_consumables == null)
                return;

            IReadOnlyList<GantryConsumables> gantries = _consumables.Gantries;
            foreach (GantryConsumables gantry in gantries)
            {
                string prefix = Prefix(gantry.Gantry, gantries.Count);

                if (!string.IsNullOrWhiteSpace(gantry.GlueSn))
                    unit.Serials[prefix + "glue_sn"] = gantry.GlueSn.Trim();
                if (!string.IsNullOrWhiteSpace(gantry.NozzleSn))
                    unit.Serials[prefix + "nozzle_sn"] = gantry.NozzleSn.Trim();

                if (!string.IsNullOrWhiteSpace(gantry.ValveSn))
                {
                    unit.Serials[prefix + "valve_sn"] = gantry.ValveSn.Trim();

                    string made = ValveManufacturingDate(gantry.ValveSn);
                    if (made != null)
                        unit.Serials[prefix + "valve_manufacturing_date"] = made;
                }
            }
        }

        /// <summary>
        /// The regression of each gantry's last calibration, its last weighing and its
        /// purge index (slide 18), from the same figures Data &gt; Glue shows.
        /// </summary>
        private void AddGlueFigures(HiveUnitData unit)
        {
            if (_dispenser == null)
                return;

            try
            {
                IReadOnlyList<GlueCartridgeStatus> cartridges = _dispenser.GetGlueCartridgesAsync().GetAwaiter().GetResult();
                IReadOnlyList<GlueRegression> regressions = _dispenser.GetGlueRegressionsAsync().GetAwaiter().GetResult();
                IReadOnlyList<GlueWeighingRecord> weighings = _dispenser
                    .GetGlueWeighingsAsync(unit.OutputTime.AddDays(-7), unit.OutputTime).GetAwaiter().GetResult();

                int gantryCount = Math.Max(_consumables != null ? _consumables.Gantries.Count : 0,
                    (cartridges ?? new GlueCartridgeStatus[0]).Count);

                foreach (GlueCartridgeStatus cartridge in cartridges ?? new GlueCartridgeStatus[0])
                {
                    // Slide 18: +1 for every unit dispensed, every purge and every weighing,
                    // back to 0 at a glue change.
                    unit.Data[Prefix(cartridge.Gantry, gantryCount) + "purge_idx"] =
                        cartridge.ProducedQty + cartridge.LongPurgeCycles + cartridge.ShortPurgeCycles + cartridge.WeighingCycles;
                }

                foreach (IGrouping<string, GlueRegression> gantry in (regressions ?? new GlueRegression[0])
                    .Where(r => r != null && r.CalibratedAt.HasValue)
                    .GroupBy(r => r.Gantry ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                {
                    GlueRegression first = gantry.OrderBy(r => r.Path).First();
                    string prefix = Prefix(gantry.Key, gantryCount);
                    unit.Data[prefix + "w"] = first.W;
                    unit.Data[prefix + "b"] = first.B;
                    unit.Data[prefix + "R2"] = first.R2;
                }

                foreach (IGrouping<string, GlueWeighingRecord> gantry in (weighings ?? new GlueWeighingRecord[0])
                    .Where(w => w != null)
                    .GroupBy(w => w.Gantry ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                {
                    GlueWeighingRecord last = gantry.OrderByDescending(w => w.Time).First();
                    string prefix = Prefix(gantry.Key, gantryCount);
                    unit.Data[prefix + "last_glue_weight"] = last.WeightMg;
                    unit.Serials[prefix + "last_weigh_time"] = HivePayload.Time(last.Time);
                }
            }
            catch (Exception ex)
            {
                Log("The glue figures of unit " + unit.UnitSn + " could not be read: " + ex.Message);
            }
        }

        /// <summary>The spec limits of every number in the payload that has a row in Config &gt; Spec Limits (Production).</summary>
        private void AddLimits(HiveUnitData unit)
        {
            if (_limits == null)
                return;

            try
            {
                foreach (AeLimitRecord row in _limits.LoadAeLimits(AeLimitKind.Production) ?? new List<AeLimitRecord>())
                {
                    string key = (row.Parameter ?? string.Empty).Trim();
                    object value;
                    if (key.Length == 0 || !unit.Data.TryGetValue(key, out value) || value is string)
                        continue;

                    double number;
                    var limit = new HiveLimit();
                    if (double.TryParse(row.Min, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                        limit.Lower = number;
                    if (double.TryParse(row.Max, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                        limit.Upper = number;

                    if (limit.Lower.HasValue || limit.Upper.HasValue)
                        unit.Limits[key] = limit;
                }
            }
            catch (Exception ex)
            {
                Log("The spec limits could not be read for unit " + unit.UnitSn + ": " + ex.Message);
            }
        }

        /// <summary>"front_" / "rear_" on a machine with two gantries (slide 16); nothing with one.</summary>
        private static string Prefix(string gantry, int gantryCount)
        {
            return gantryCount > 1 && !string.IsNullOrWhiteSpace(gantry)
                ? gantry.Trim().ToLowerInvariant() + "_"
                : string.Empty;
        }

        /// <summary>
        /// Slide 15: "keep only numeric values from the SW version name and add decimal
        /// between version numbers and YYMMDD" - APL_1.0.1.1_251216_POR is 1011.251216.
        /// </summary>
        internal static double? VersionDigits(string softwareVersion)
        {
            string[] parts = (softwareVersion ?? string.Empty).Split('_');
            if (parts.Length < 3)
                return null;

            string version = new string(parts[1].Where(char.IsDigit).ToArray());
            string date = new string(parts[2].Where(char.IsDigit).ToArray());
            if (version.Length == 0 || date.Length != 6)
                return null;

            double value;
            return double.TryParse(version + "." + date, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : (double?)null;
        }

        /// <summary>Slide 18: YYMMDD parsed from the standard valve SN XXX-YYY-001_YYMMDD.</summary>
        internal static string ValveManufacturingDate(string valveSn)
        {
            int underscore = (valveSn ?? string.Empty).LastIndexOf('_');
            if (underscore < 0)
                return null;

            string digits = new string(valveSn.Substring(underscore + 1).Where(char.IsDigit).ToArray());
            if (digits.Length == 8)
                digits = digits.Substring(2);   // YYYYMMDD on the label

            return digits.Length == 6 ? digits : null;
        }

        /// <summary>
        /// The MAC address of the adapter this machine talks to the network with, as
        /// "C4:84:FC:07:3D:7D". Read from the adapter, not configured.
        /// </summary>
        private static string ReadMacAddress()
        {
            try
            {
                NetworkInterface adapter = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                             && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                             && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                             && n.GetPhysicalAddress().GetAddressBytes().Length == 6)
                    .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0)
                    .FirstOrDefault();

                if (adapter == null)
                    return null;

                var text = new StringBuilder();
                foreach (byte part in adapter.GetPhysicalAddress().GetAddressBytes())
                {
                    if (text.Length > 0)
                        text.Append(':');
                    text.Append(part.ToString("X2", CultureInfo.InvariantCulture));
                }

                return text.ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>One optional PLC value: a blank map entry or a failed read leaves the key out.</summary>
        private static T Try<T>(Func<T> read, string what)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                Log("The unit's " + what + " could not be read from the PLC: " + ex.Message);
                return default(T);
            }
        }

        private static void Skip(string why)
        {
            Log("[Error/Alert] A finished unit was not sent to HIVE: " + why + ".");
        }

        private static void Log(string message)
        {
            Logger.WriteLog(LogType.Error, "Hive", "UnitData", message, string.Empty);
        }
    }
}
