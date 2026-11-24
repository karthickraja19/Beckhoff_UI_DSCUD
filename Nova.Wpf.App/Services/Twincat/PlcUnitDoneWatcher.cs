using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nova.Abstractions.Data.Repositories;
using Nova.Abstractions.Services;
using Nova.Hive;
using Nova.Alarm;
using Nova.Wpf.App.Services.Twincat.Mapping;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// What happens when the PLC finishes a unit. The PLC counts finished units up in one
    /// variable (symbol map: Status.UnitDoneCount) and holds the unit's data until the next
    /// one; for each step up this
    /// <list type="number">
    /// <item>saves the unit's production row (UnitRecord) into TEAL_Master - the rows the
    /// Home I/O summary and the Data page's I/O, Tossing and Yield tabs read,</item>
    /// <item>saves its measured coordinates (Coordinates) for the Data page's Axis tab,</item>
    /// <item>sends it to HIVE as machine_data (<see cref="HiveUnitDataBuilder"/>,
    /// <see cref="HiveService.SendUnit"/>),</item>
    /// <item>hands it to HIVE (<see cref="HiveService.UnitProcessed"/>) for the debug-unit
    /// limits and the audit rounds.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Each part runs only when its variables are mapped. With the counter blank nothing is
    /// watched - the PLC has no such signal yet - and the startup warnings say so. When the
    /// counter jumps by more than one (several units in one notification) only the last unit's
    /// data is still in the PLC: that one is saved, and every one is counted for HIVE.
    /// </remarks>
    public sealed class PlcUnitDoneWatcher
    {
        public const string CoordinateKind = "Coordinates";

        public static readonly string[] CoordinateColumns = { "UnitSn", "Point", "Axis", "Value", "Nominal", "Lsl", "Usl" };

        private readonly TwincatPlcConnection _plc;
        private readonly HiveService _hive;
        private readonly IProductionRecorder _production;
        private readonly MachineRecordStore _coordinates;
        private readonly StatusSymbols _status;
        private readonly UnitRecordSymbols _unit;
        private readonly List<CoordinatePointSymbol> _points;
        private readonly PlcEventCounter _counter;
        private readonly HiveUnitDataBuilder _unitData;
        private readonly NgStatistics _ngStatistics;
        private readonly Action<HiveUnitData> _beforeSend;
        private readonly Action<string, bool> _unitPassed;

        /// <param name="unitData">Builds the unit's machine_data payload; null sends none.</param>
        /// <param name="ngStatistics">Counts the unit for the 3 consecutive / 5 total NG rule (T6); null counts nothing.</param>
        /// <param name="beforeSend">Runs on the unit's payload before it goes to HIVE, e.g. to add its picture zip (T5).</param>
        /// <param name="unitPassed">Told each unit that left the station with its serial number and result, e.g. for the early warning (S17).</param>
        public PlcUnitDoneWatcher(
            TwincatPlcConnection plc, PlcSubscriptions subscriptions, HiveService hive, PlcSymbolMap map,
            IProductionRecorder production, MachineRecordStore coordinates, HiveUnitDataBuilder unitData = null,
            NgStatistics ngStatistics = null, Action<HiveUnitData> beforeSend = null, Action<string, bool> unitPassed = null)
        {
            _ngStatistics = ngStatistics;
            _beforeSend = beforeSend;
            _unitPassed = unitPassed;
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _hive = hive ?? throw new ArgumentNullException(nameof(hive));
            _unitData = unitData;
            map = map ?? new PlcSymbolMap();
            _status = map.Status;
            _unit = map.UnitRecord;
            _points = (map.Coordinates.Points ?? new List<CoordinatePointSymbol>())
                .Where(p => p != null && PlcSymbolMap.Has(p.Symbol))
                .ToList();
            _production = _unit.IsMapped ? production : null;
            _coordinates = _points.Count > 0 ? coordinates : null;
            _counter = new PlcEventCounter(subscriptions, _status.UnitDoneCount, "finished unit", OnUnitsFinished);
        }

        /// <summary>False while the PLC has no finished-unit counter in the map.</summary>
        public bool IsWired => _counter.IsWired;

        public void Start()
        {
            _counter.Start();
        }

        /// <summary>Runs on a worker thread, in order: the PLC still holds the last unit's data.</summary>
        private void OnUnitsFinished(int units)
        {
            DateTime now = DateTime.Now;
            string serial = string.Empty;
            try
            {
                serial = Convert.ToString(PlcValues.Read(_plc, _status.UnitDoneSerial), CultureInfo.InvariantCulture) ?? string.Empty;
                serial = serial.Trim();
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "PLC", "UnitDone",
                    "The finished unit's serial number (" + _status.UnitDoneSerial + ") could not be read: " + ex.Message, string.Empty);
            }

            RecordProduction(serial, now);
            RecordCoordinates(serial, now);
            SendToHive(serial, now, units);
            bool? pass = CountForNgRule(serial);

            // HIVE counts every unit; only the last one has a serial number left in the PLC.
            for (int i = 1; i <= units; i++)
                _hive.UnitProcessed(i == units ? serial : string.Empty);

            if (_unitPassed != null && serial.Length > 0)
            {
                try
                {
                    _unitPassed(serial, pass ?? true);
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "PLC", "UnitDone",
                        "Unit " + serial + " could not be handed on after it finished: " + ex.Message, ex.ToString());
                }
            }
        }

        /// <summary>
        /// T6: the unit's final result goes to the 3 consecutive / 5 total NG count, with the
        /// station that dispensed it and its failure code. Needs UnitRecord.Pass.
        /// </summary>
        /// <returns>The unit's result, or null when the PLC does not give one.</returns>
        private bool? CountForNgRule(string serial)
        {
            if (!_unit.IsMapped)
                return null;

            try
            {
                bool? pass = PlcValues.Flag(_plc, _unit.Pass);
                if (!pass.HasValue || _ngStatistics == null)
                    return pass;

                double? station = PlcValues.Number(_plc, _unit.Station);
                string failure = pass.Value ? null : PlcValues.Text(_plc, _unit.FailureCode);
                _ngStatistics.UnitFinished(serial, pass.Value, station.HasValue ? (int)Math.Round(station.Value) : (int?)null, failure);
                return pass;
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "PLC", "UnitDone",
                    "The result of unit " + serial + " could not be read for the NG count: " + ex.Message, ex.ToString());
                return null;
            }
        }

        /// <summary>
        /// The unit's machine_data payload (DFM S1.2). The values are read here, while the
        /// PLC still holds them; the upload itself runs behind, so it does not hold up the
        /// next unit. Before <see cref="HiveService.UnitProcessed"/>, so an audit unit
        /// carries the operator it was tested by.
        /// </summary>
        private void SendToHive(string serial, DateTime now, int units)
        {
            if (_unitData == null || _unitData.UnavailableReason != null)
                return;

            try
            {
                if (units > 1)
                {
                    Logger.WriteLog(LogType.Error, "Hive", "UnitData",
                        "[Error/Alert] " + (units - 1) + " finished unit(s) were not sent to HIVE: the PLC counted "
                        + units + " units at once and holds only the last one's data.", string.Empty);
                }

                HiveUnitData unit = _unitData.Build(serial, now);
                if (unit != null)
                {
                    if (_beforeSend != null)
                        _beforeSend(unit);

                    _hive.SendUnit(unit);
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "UnitData",
                    "Unit " + serial + " could not be sent to HIVE: " + ex.Message, ex.ToString());
            }
        }

        private void RecordProduction(string serial, DateTime now)
        {
            if (_production == null)
                return;

            try
            {
                bool? pass = PlcValues.Flag(_plc, _unit.Pass);
                if (!pass.HasValue)
                    return;

                double cycle = PlcValues.Number(_plc, _unit.CycleTime) ?? 0;
                double? station = PlcValues.Number(_plc, _unit.Station);
                var record = new ProductionUnitRecord
                {
                    ProductCode = serial,
                    IsPass = pass.Value,
                    CycleTimeSeconds = cycle,
                    ProductionTime = now.AddSeconds(-Math.Max(0, cycle)),
                    Iteration = (int)(PlcValues.Number(_plc, _unit.Iteration) ?? 1),
                    TossingCount = PlcValues.Flag(_plc, _unit.Tossing) == true ? 1 : 0,
                    FailureCode = PlcValues.Text(_plc, _unit.FailureCode),
                    Gantry = station.HasValue ? "Station " + station.Value.ToString("0", CultureInfo.InvariantCulture) : null,
                };

                _production.RecordAsync(record).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "PLC", "UnitDone",
                    "The production row of unit " + serial + " could not be saved: " + ex.Message, ex.ToString());
            }
        }

        private void RecordCoordinates(string serial, DateTime now)
        {
            if (_coordinates == null)
                return;

            foreach (CoordinatePointSymbol point in _points)
            {
                try
                {
                    double? value = PlcValues.Number(PlcValues.Read(_plc, point.Symbol));
                    if (!value.HasValue)
                        continue;

                    _coordinates.Append(now, new[]
                    {
                        serial,
                        point.Point ?? string.Empty,
                        point.Axis ?? string.Empty,
                        MachineRecordStore.Number(value),
                        MachineRecordStore.Number(point.Nominal),
                        MachineRecordStore.Number(point.Lsl),
                        MachineRecordStore.Number(point.Usl),
                    });
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "PLC", "UnitDone",
                        "Coordinate " + point.Point + " " + point.Axis + " of unit " + serial + " could not be saved: " + ex.Message, ex.ToString());
                }
            }
        }
    }

    /// <summary>
    /// <see cref="ICoordinateDataSource"/> over the coordinates <see cref="PlcUnitDoneWatcher"/>
    /// saved for each finished unit.
    /// </summary>
    public sealed class RecordedCoordinateDataSource : ICoordinateDataSource
    {
        private readonly MachineRecordStore _store;

        public RecordedCoordinateDataSource(MachineRecordStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public System.Threading.Tasks.Task<IReadOnlyList<CoordinateRecord>> QueryAsync(DateTime start, DateTime end)
        {
            return System.Threading.Tasks.Task.Run<IReadOnlyList<CoordinateRecord>>(() => _store.Query(start, end)
                .Select(row => new CoordinateRecord
                {
                    Time = row.Key,
                    UnitSn = MachineRecordStore.Field(row.Value, "UnitSn"),
                    Point = MachineRecordStore.Field(row.Value, "Point"),
                    Axis = MachineRecordStore.Field(row.Value, "Axis"),
                    Value = MachineRecordStore.ParseNumber(row.Value, "Value") ?? 0,
                    Nominal = MachineRecordStore.ParseNumber(row.Value, "Nominal"),
                    Lsl = MachineRecordStore.ParseNumber(row.Value, "Lsl"),
                    Usl = MachineRecordStore.ParseNumber(row.Value, "Usl"),
                })
                .ToList());
        }
    }
}
