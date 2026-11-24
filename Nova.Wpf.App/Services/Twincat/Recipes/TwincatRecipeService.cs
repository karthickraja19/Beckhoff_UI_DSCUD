using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Nova.Wpf.App.Services.Twincat.Mapping;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// <see cref="IPlcRecipeService"/> over the PLC's recipe block, FB_RecipeMapping
    /// (symbol map: Recipe.Gantry, e.g. MAIN.recipe.Recipe): one FB_Recipe per gantry
    /// (E_Recipe), which the PLC sequences drive every taught move from
    /// (FUN_AuxServoMoveToPos). A gantry module's recipe is kept as V3 keeps it: the module's
    /// servos are listed in Data\Gantry.json, and its positions are the ConfiguredPositions
    /// of those servos in Data\Servos.json. In the PLC's table a row is a position (name
    /// order as <see cref="IRecipeService.LoadGantryPositions"/> gives them) and a column a
    /// servo, by its Type in Gantry.json.
    /// </summary>
    /// <remarks>
    /// FB_Recipe, as the PLC declares it:
    /// <code>
    /// Step      : ARRAY[1..MAX_POS, 1..MAX_AXES] OF ST_AxisMove;   // rPos, rVel, rAcc, rDec : LREAL
    /// Name      : ARRAY[1..MAX_POS] OF STRING(30);
    /// nPosUsed  : INT;    // rows written
    /// nAxisUsed : INT;    // axes written
    /// bLoaded   : BOOL;   // written LAST: until TRUE the sequences do not move
    /// </code>
    /// The axis number is E_Servo (X_AXIS = 1, Y_AXIS = 2, Z_AXIS = 3, R_AXIS = 4), local to
    /// the gantry: the servo whose Type is X is always axis 1 whichever gantry it is. An axis
    /// the module does not have, between two it has, and a servo that has no position of a
    /// row's name, are sent as zeros - speed 0, which the PLC takes as "not taught". rPos,
    /// rVel, rAcc and rDec are the position's Value, Speed, Acceleration and Deceleration.
    /// The module's gantry is its PlcGantry in Gantry.json; a module without one is not written.
    ///
    /// bLoaded is cleared first, so a sequence never drives from a half-written recipe, then
    /// Step and Name go over in one write each, then nPosUsed and nAxisUsed, then bLoaded.
    /// The rows and axes the table does not use are zeroed, so nothing of a longer recipe
    /// written before is left behind. MAX_POS and MAX_AXES are taken from the sizes the PLC
    /// declares Name and Step with, so a bigger PLC table needs no change here.
    ///
    /// A robot recipe row (Data\RobotRecipe.json) that names a "PlcMember" goes to that member
    /// of every configured robot, RobotGVL.Robots[Id].{PlcMember} (symbol map: Recipe.Robot,
    /// Id from Data\Robots.json), e.g. "cmd.robotSpeed": the Epson SPEED, a BYTE that
    /// FB_RobotSD passes to the robot (GIB101_ROBOT_SPEED) only in AUTO_RUNNING. The value is
    /// converted to the type the PLC declares the member with, and checked against the row's
    /// optional "Min" and "Max". A row without a PlcMember stays in the HMI. So a new robot
    /// value needs its PLC member and a row naming it - no change here.
    ///
    /// A CNC channel's named positions (Data\CncRecipe.json) go to the same kind of table:
    /// the FB_Recipe the symbol map gives the channel (Cnc.Channels.n.RecipeGantry), which
    /// is where the station's sequence reads them from. Row = position; the column of each
    /// axis is its number in the channel (Cnc.AxisIndex). Only rPos carries a value; rVel,
    /// rAcc and rDec are the map's TaughtVelocity, because the PLC takes speed 0 as "not
    /// taught" and the feed itself is in the CNC program.
    ///
    /// The PLC does not keep the recipe over a restart, so every mapped gantry, the robot
    /// values and every mapped CNC channel are written again each time the PLC comes back to
    /// Run; the Recipe page writes a gantry, the robot values or a CNC channel on Save. The writes are made one at a time, in
    /// order, on a worker thread.
    /// </remarks>
    public sealed class TwincatRecipeService : IPlcRecipeService
    {
        private const int AxisMoveSize = 4 * sizeof(double);    // ST_AxisMove
        private const int NameLength = 30;                       // STRING(30)
        private const int NameSize = NameLength + 1;             // and its terminating 0

        /// <summary>The axes in E_Servo order: X_AXIS = 1 ... R_AXIS = 4.</summary>
        private static readonly string[] AxisColumns = { "X", "Y", "Z", "R" };

        private static readonly Encoding PlcText = Encoding.GetEncoding(1252);

        // A robot recipe row's PLC mapping: properties of the row in Data\RobotRecipe.json,
        // not columns, so the Robot tab does not show them.
        private const string PlcMemberKey = "PlcMember";   // member of RobotGVL.Robots[Id], e.g. cmd.robotSpeed
        private const string MinKey = "Min";               // optional, inclusive
        private const string MaxKey = "Max";               // optional, inclusive

        private readonly TwincatPlcConnection _plc;
        private readonly PlcSubscriptions _subscriptions;
        private readonly IRecipeService _recipes;
        private readonly string _recipeSymbol;
        private readonly IReadOnlyList<int> _robotIds;
        private readonly string _robotSymbol;
        private readonly CncSymbols _cnc;
        private readonly object _sync = new object();

        private Task _writes = Task.CompletedTask;   // the PLC writes waiting to be made, in order
        private bool _wasLive;

        public TwincatRecipeService(
            TwincatPlcConnection plc, PlcSubscriptions subscriptions, IRecipeService recipes, string recipeSymbol,
            IReadOnlyList<int> robotIds, string robotSymbol, CncSymbols cnc)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            _recipeSymbol = (recipeSymbol ?? string.Empty).Trim();
            _robotIds = robotIds ?? new int[0];
            _robotSymbol = (robotSymbol ?? string.Empty).Trim();
            _cnc = cnc ?? new CncSymbols();

            if (_recipeSymbol.Length > 0 || _robotSymbol.Length > 0)
                _subscriptions.LiveChanged += OnLiveChanged;
        }

        public bool IsAvailable => _recipeSymbol.Length > 0;

        /// <summary>True when the map gives at least one CNC channel a recipe table.</summary>
        private bool HasCncRecipes => _recipeSymbol.Length > 0
            && _cnc.Channels != null
            && _cnc.Channels.Values.Any(c => c != null && c.RecipeGantry >= 1);

        public string UnavailableReason => IsAvailable ? string.Empty : "Recipe.Gantry is blank in Data\\" + PlcSymbolMap.FileName + ".";

        /// <summary>Writes every mapped gantry and the robot values if the PLC is already live; from then on, each time it comes back.</summary>
        public void Start()
        {
            OnLiveChanged(this, EventArgs.Empty);
        }

        public Task WriteGantryRecipeAsync(string moduleName)
        {
            if (!IsAvailable)
                return Task.FromException(new InvalidOperationException(UnavailableReason));
            if (string.IsNullOrWhiteSpace(moduleName))
                return Task.FromException(new ArgumentException("Module name is required.", nameof(moduleName)));

            return QueueWrite(() => WriteGantry(moduleName));
        }

        public Task WriteRobotRecipeAsync()
        {
            if (_robotSymbol.Length == 0)
                return Task.FromException(new InvalidOperationException("This machine has no robot in the PLC: Recipe.Robot is blank in Data\\" + PlcSymbolMap.FileName + "."));

            return QueueWrite(WriteRobotValues);
        }

        public Task WriteCncRecipeAsync(string channelName)
        {
            if (!HasCncRecipes)
                return Task.FromException(new InvalidOperationException(
                    "The CNC positions have nowhere to go in the PLC: Recipe.Gantry or Cnc.Channels is blank in Data\\" + PlcSymbolMap.FileName + "."));
            if (string.IsNullOrWhiteSpace(channelName))
                return Task.FromException(new ArgumentException("Channel name is required.", nameof(channelName)));

            return QueueWrite(() => WriteCnc(CncChannel(channelName)));
        }

        /// <summary>Raised on the ADS or a worker thread: only queues the writes.</summary>
        private void OnLiveChanged(object sender, EventArgs e)
        {
            lock (_sync)
            {
                bool live = _subscriptions.IsLive;
                bool cameBack = live && !_wasLive;
                _wasLive = live;
                if (!cameBack)
                    return;
            }

            QueueWrite(WriteAll);
        }

        private void WriteAll()
        {
            if (IsAvailable)
                WriteAllGantries();

            if (_robotSymbol.Length > 0)
            {
                try
                {
                    WriteRobotValues();
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "PLC", "Recipe",
                        "The robot recipe could not be written to the PLC: " + ex.Message, ex.ToString());
                }
            }

            if (HasCncRecipes)
                WriteAllCnc();
        }

        private void WriteAllGantries()
        {
            foreach (string module in _recipes.GetGantryModules())
            {
                if (_recipes.GetGantryModuleDefinition(module).PlcGantry < 1)
                {
                    Logger.WriteLog(LogType.Info, "PLC", "Recipe",
                        module + " has no PlcGantry in Gantry.json - its recipe is not written to the PLC.", string.Empty);
                    continue;
                }

                try
                {
                    WriteGantry(module);
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "PLC", "Recipe",
                        "The " + module + " recipe could not be written to the PLC: " + ex.Message, ex.ToString());
                }
            }
        }

        // ==================================================================
        // One gantry
        // ==================================================================

        private void WriteGantry(string moduleName)
        {
            int gantry = _recipes.GetGantryModuleDefinition(moduleName).PlcGantry;
            if (gantry < 1)
                throw new InvalidOperationException(moduleName + " is not mapped to a PLC gantry. Set its \"PlcGantry\" (the PLC's E_Recipe number, 1-8) in Data\\Gantry.json.");

            if (!_plc.IsConnected)
                throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");

            string recipe = _recipeSymbol + "[" + gantry + "]";
            int maxPositions;
            int maxAxes;
            ReadTableSize(recipe, out maxPositions, out maxAxes);

            // The module's servos (Gantry.json) by their E_Servo column: the axis Type.
            GantryModuleDefinition definition = _recipes.GetGantryModuleDefinition(moduleName);
            var axisOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (GantryAxisDefinition axis in definition.Axes)
            {
                int index = Array.IndexOf(AxisColumns, axis.Type) + 1;
                if (index >= 1)
                    axisOf[axis.Name] = index;
            }

            // nAxisUsed: up to the last axis the module has.
            int axesUsed = axisOf.Count > 0 ? axisOf.Values.Max() : 0;
            IReadOnlyList<GantryPosition> rows = _recipes.LoadGantryPositions(moduleName);

            if (rows.Count == 0)
                throw new InvalidOperationException("the " + moduleName + " recipe has no positions (no ConfiguredPositions on its servos in Data\\Servos.json).");
            if (axesUsed == 0)
                throw new InvalidOperationException(moduleName + " has no X, Y, Z or R axis in Data\\Gantry.json.");
            if (rows.Count > maxPositions)
                throw new InvalidOperationException("the " + moduleName + " recipe has " + rows.Count + " positions; the PLC holds " + maxPositions + " (MAX_POS).");
            if (axesUsed > maxAxes)
                throw new InvalidOperationException("the " + moduleName + " recipe has " + axesUsed + " axes; the PLC holds " + maxAxes + " (MAX_AXES).");

            var step = new byte[maxPositions * maxAxes * AxisMoveSize];
            var names = new byte[maxPositions * NameSize];
            for (int row = 0; row < rows.Count; row++)
            {
                PutName(names, row * NameSize, rows[row].Name);

                foreach (var axis in axisOf)
                {
                    GantryAxisMove move;
                    if (rows[row].Axes == null || !rows[row].Axes.TryGetValue(axis.Key, out move) || move == null)
                        continue;   // the servo has no such position: sent as zeros, not taught

                    int offset = (row * maxAxes + (axis.Value - 1)) * AxisMoveSize;   // Step[row, axis], the axis index the faster
                    PutReal(step, offset, move.Value);
                    PutReal(step, offset + 8, move.Speed);
                    PutReal(step, offset + 16, move.Acceleration);
                    PutReal(step, offset + 24, move.Deceleration);
                }
            }

            lock (_plc.SyncRoot)
            {
                _plc.WriteSymbol(recipe + ".bLoaded", false);
                _plc.WriteBytes(recipe + ".Step", step);
                _plc.WriteBytes(recipe + ".Name", names);
                _plc.WriteSymbol(recipe + ".nPosUsed", (short)rows.Count);
                _plc.WriteSymbol(recipe + ".nAxisUsed", (short)axesUsed);
                _plc.WriteSymbol(recipe + ".bLoaded", true);
            }

            Logger.WriteLog(LogType.Info, "PLC", "Recipe",
                "The " + moduleName + " recipe (" + rows.Count + " positions, " + axesUsed + " axes) was written to " + recipe + ".", string.Empty);
        }

        /// <summary>
        /// MAX_POS and MAX_AXES of the PLC's FB_Recipe, from the sizes it declares Name
        /// (MAX_POS x STRING(30)) and Step (MAX_POS x MAX_AXES x ST_AxisMove) with.
        /// </summary>
        private void ReadTableSize(string recipe, out int maxPositions, out int maxAxes)
        {
            int? nameBytes = _plc.SymbolSize(recipe + ".Name");
            int? stepBytes = _plc.SymbolSize(recipe + ".Step");
            if (nameBytes == null || stepBytes == null)
                throw new InvalidOperationException(recipe + " is not in the PLC (no Name or Step) - check Recipe.Gantry in Data\\" + PlcSymbolMap.FileName + " and the gantry number.");

            maxPositions = nameBytes.Value / NameSize;
            maxAxes = maxPositions > 0 ? stepBytes.Value / (maxPositions * AxisMoveSize) : 0;
            if (maxPositions < 1 || maxAxes < 1
                || nameBytes.Value != maxPositions * NameSize
                || stepBytes.Value != maxPositions * maxAxes * AxisMoveSize)
            {
                throw new InvalidOperationException(recipe + ".Name (" + nameBytes + " bytes) and .Step (" + stepBytes
                    + " bytes) are not ARRAY OF STRING(30) and ARRAY[pos, axis] OF ST_AxisMove (4 x LREAL).");
            }
        }

        // ==================================================================
        // The robots
        // ==================================================================

        /// <summary>A Robot recipe row that names a PLC member, as the row holds it.</summary>
        private sealed class RobotValue
        {
            public string Point;    // the row's point name
            public string Raw;      // its value
            public string Member;   // PlcMember
            public string Min;      // blank: no limit
            public string Max;
        }

        private void WriteRobotValues()
        {
            if (_robotIds.Count == 0)
                throw new InvalidOperationException("no robot is configured in Data\\Robots.json.");

            List<RobotValue> rows = RobotValues();
            if (rows.Count == 0)
                throw new InvalidOperationException("no row of the Robot recipe names a " + PlcMemberKey + " in Data\\RobotRecipe.json.");

            if (!_plc.IsConnected)
                throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");

            lock (_plc.SyncRoot)
            {
                // Every value is converted to its member's PLC type and checked before any is
                // written, so a bad value or a missing member writes nothing and the robots
                // never run half-updated.
                var writes = new List<KeyValuePair<string, object>>();
                foreach (int id in _robotIds)
                {
                    foreach (RobotValue row in rows)
                    {
                        string symbol = _robotSymbol + "[" + id + "]." + row.Member;
                        string plcType = _plc.SymbolTypeName(symbol);
                        if (plcType == null)
                        {
                            throw new InvalidOperationException(symbol + " is not in the PLC - check the " + row.Point + " row's " + PlcMemberKey
                                + " in Data\\RobotRecipe.json, Recipe.Robot in Data\\PlcSymbols.json and the robot's Id in Data\\Robots.json.");
                        }

                        writes.Add(new KeyValuePair<string, object>(symbol, PlcValue(row, plcType)));
                    }
                }

                foreach (KeyValuePair<string, object> write in writes)
                    _plc.WriteSymbol(write.Key, write.Value);
            }

            Logger.WriteLog(LogType.Info, "PLC", "Recipe",
                "The robot recipe (" + string.Join(", ", rows.Select(r => r.Point + " " + r.Raw)) + ") was written to "
                + _robotIds.Count + " robot(s) at " + _robotSymbol + ".", string.Empty);
        }

        /// <summary>The Robot recipe's rows that name a PlcMember. The point name is the first column, the value the second.</summary>
        private List<RobotValue> RobotValues()
        {
            DynamicTableDocument table = _recipes.LoadRobotRecipeDynamic();
            IReadOnlyList<DynamicColumnSchema> columns = table?.Columns ?? new DynamicColumnSchema[0];
            string pointKey = columns.Count > 0 ? columns[0].Key : "PointName";
            string valueKey = columns.Count > 1 ? columns[1].Key : "Value";

            return (table?.Rows ?? new Dictionary<string, string>[0])
                .Where(row => Cell(row, PlcMemberKey).Length > 0)
                .Select(row => new RobotValue
                {
                    Point = Cell(row, pointKey),
                    Raw = Cell(row, valueKey),
                    Member = Cell(row, PlcMemberKey),
                    Min = Cell(row, MinKey),
                    Max = Cell(row, MaxKey),
                })
                .ToList();
        }

        /// <summary>
        /// The row's value as the .NET type of the PLC type its member is declared with, within
        /// that type's range and the row's Min and Max. Throws for anything else.
        /// </summary>
        private static object PlcValue(RobotValue row, string plcType)
        {
            string type = plcType.Trim().ToUpperInvariant();
            if (type == "BOOL")
            {
                if (row.Raw == "1" || string.Equals(row.Raw, "TRUE", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (row.Raw == "0" || string.Equals(row.Raw, "FALSE", StringComparison.OrdinalIgnoreCase))
                    return false;
                throw BadRobotValue(row, "TRUE/FALSE or 1/0");
            }

            double value;
            if (!TryNumber(row.Raw, out value))
                throw BadRobotValue(row, "a number");

            double min = RobotLimit(row, row.Min, MinKey, double.MinValue);
            double max = RobotLimit(row, row.Max, MaxKey, double.MaxValue);
            if (value < min || value > max)
            {
                throw BadRobotValue(row, row.Min.Length == 0 ? "at most " + row.Max
                    : row.Max.Length == 0 ? "at least " + row.Min
                    : "from " + row.Min + " to " + row.Max);
            }

            switch (type)
            {
                case "BYTE":
                case "USINT":
                    return (byte)Whole(row, value, byte.MinValue, byte.MaxValue);
                case "SINT":
                    return (sbyte)Whole(row, value, sbyte.MinValue, sbyte.MaxValue);
                case "WORD":
                case "UINT":
                    return (ushort)Whole(row, value, ushort.MinValue, ushort.MaxValue);
                case "INT":
                    return (short)Whole(row, value, short.MinValue, short.MaxValue);
                case "DWORD":
                case "UDINT":
                    return (uint)Whole(row, value, uint.MinValue, uint.MaxValue);
                case "DINT":
                    return (int)Whole(row, value, int.MinValue, int.MaxValue);
                case "REAL":
                    return (float)value;
                case "LREAL":
                    return value;
                default:
                    throw new InvalidOperationException("the Robot recipe's " + row.Point + " goes to " + row.Member + ", a " + plcType
                        + " in the PLC; only BOOL, BYTE, SINT, USINT, WORD, INT, UINT, DWORD, DINT, UDINT, REAL and LREAL can be written.");
            }
        }

        private static long Whole(RobotValue row, double value, long min, long max)
        {
            if (value != Math.Floor(value) || value < min || value > max)
                throw BadRobotValue(row, "a whole number from " + min + " to " + max);

            return (long)value;
        }

        /// <summary>The row's Min or Max; a blank one is no limit.</summary>
        private static double RobotLimit(RobotValue row, string raw, string key, double none)
        {
            if (raw.Length == 0)
                return none;

            double limit;
            if (!TryNumber(raw, out limit))
                throw new InvalidOperationException("the Robot recipe's " + row.Point + " row has " + key + " '" + raw + "', not a number.");

            return limit;
        }

        private static InvalidOperationException BadRobotValue(RobotValue row, string expected)
        {
            return new InvalidOperationException("the Robot recipe's " + row.Point + " is '" + row.Raw + "'; it must be " + expected + ".");
        }

        // ==================================================================
        // The CNC channels
        // ==================================================================

        private void WriteAllCnc()
        {
            IReadOnlyList<CncChannelRecipe> channels;
            try
            {
                channels = _recipes.LoadCncRecipe();
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "PLC", "Recipe",
                    "The CNC recipe could not be read: " + ex.Message, ex.ToString());
                return;
            }

            foreach (CncChannelRecipe channel in channels)
            {
                if (channel.PlcChannel < 1)
                {
                    Logger.WriteLog(LogType.Info, "PLC", "Recipe",
                        channel.Name + " has no PlcChannel in CncRecipe.json - its positions are not written to the PLC.", string.Empty);
                    continue;
                }

                try
                {
                    WriteCnc(channel);
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "PLC", "Recipe",
                        "The " + channel.Name + " CNC recipe could not be written to the PLC: " + ex.Message, ex.ToString());
                }
            }
        }

        private CncChannelRecipe CncChannel(string channelName)
        {
            CncChannelRecipe channel = _recipes.LoadCncRecipe()
                .FirstOrDefault(c => string.Equals((c.Name ?? string.Empty).Trim(), channelName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (channel == null)
                throw new InvalidOperationException("the CNC recipe has no channel '" + channelName + "'.");

            return channel;
        }

        private void WriteCnc(CncChannelRecipe channel)
        {
            CncChannelSymbols map = channel.PlcChannel >= 1 ? _cnc.ChannelMap(channel.PlcChannel) : null;
            if (map == null || map.RecipeGantry < 1)
            {
                throw new InvalidOperationException(channel.Name + " has no recipe table in the PLC. Give its \"PlcChannel\" (Data\\CncRecipe.json) "
                    + "a RecipeGantry under Cnc.Channels in Data\\" + PlcSymbolMap.FileName + ".");
            }

            if (!_plc.IsConnected)
                throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");

            string recipe = _recipeSymbol + "[" + map.RecipeGantry + "]";
            List<CncPositionRecipe> rows = channel.Positions ?? new List<CncPositionRecipe>();
            if (rows.Count == 0)
                throw new InvalidOperationException("the " + channel.Name + " CNC recipe has no positions.");

            // The column of each axis is its number in the channel.
            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string axis in CncChannelRecipe.AxisNames)
            {
                int index;
                if (_cnc.AxisIndex != null && _cnc.AxisIndex.TryGetValue(axis, out index) && index >= 1)
                    columns[axis] = index;
            }

            if (columns.Count == 0)
                throw new InvalidOperationException("no CNC axis has a number (Cnc.AxisIndex in Data\\" + PlcSymbolMap.FileName + ").");

            int axesUsed = columns.Values.Max();
            int maxPositions;
            int maxAxes;
            ReadTableSize(recipe, out maxPositions, out maxAxes);
            if (rows.Count > maxPositions)
                throw new InvalidOperationException("the " + channel.Name + " CNC recipe has " + rows.Count + " positions; the PLC holds " + maxPositions + " (MAX_POS).");
            if (axesUsed > maxAxes)
                throw new InvalidOperationException("the CNC axes are numbered up to " + axesUsed + "; the PLC's recipe holds " + maxAxes + " (MAX_AXES).");

            double taught = _cnc.TaughtVelocity > 0 ? _cnc.TaughtVelocity : 1.0;
            var step = new byte[maxPositions * maxAxes * AxisMoveSize];
            var names = new byte[maxPositions * NameSize];
            for (int row = 0; row < rows.Count; row++)
            {
                PutName(names, row * NameSize, rows[row].Name);
                foreach (var column in columns)
                {
                    double value;
                    if (rows[row].Values == null || !rows[row].Values.TryGetValue(column.Key, out value))
                        continue;   // sent as zeros: not taught

                    int offset = (row * maxAxes + (column.Value - 1)) * AxisMoveSize;   // Step[row, axis]
                    PutReal(step, offset, value);
                    PutReal(step, offset + 8, taught);
                    PutReal(step, offset + 16, taught);
                    PutReal(step, offset + 24, taught);
                }
            }

            lock (_plc.SyncRoot)
            {
                _plc.WriteSymbol(recipe + ".bLoaded", false);
                _plc.WriteBytes(recipe + ".Step", step);
                _plc.WriteBytes(recipe + ".Name", names);
                _plc.WriteSymbol(recipe + ".nPosUsed", (short)rows.Count);
                _plc.WriteSymbol(recipe + ".nAxisUsed", (short)axesUsed);
                _plc.WriteSymbol(recipe + ".bLoaded", true);
            }

            Logger.WriteLog(LogType.Info, "PLC", "Recipe",
                "The " + channel.Name + " CNC recipe (" + rows.Count + " positions) was written to " + recipe + ".", string.Empty);
        }

        private static string Cell(Dictionary<string, string> row, string key)
        {
            string value;
            return row != null && row.TryGetValue(key, out value) && value != null ? value.Trim() : string.Empty;
        }

        private static bool TryNumber(string raw, out double value)
        {
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private static void PutReal(byte[] buffer, int offset, double value)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, buffer, offset, sizeof(double));
        }

        /// <summary>A STRING(30): at most 30 characters, the rest of its bytes 0. A longer name is cut - it is only shown.</summary>
        private static void PutName(byte[] buffer, int offset, string name)
        {
            byte[] text = PlcText.GetBytes(name ?? string.Empty);
            Buffer.BlockCopy(text, 0, buffer, offset, Math.Min(text.Length, NameLength));
        }

        /// <summary>
        /// Runs the write after every write queued before it, on a worker thread. A failure is
        /// caught and logged here and handed to the caller through the returned task, so it
        /// never leaves the write unhandled (which the debugger stops on).
        /// </summary>
        private Task QueueWrite(Action write)
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync)
            {
                _writes = _writes.ContinueWith(_ =>
                {
                    try
                    {
                        write();
                        done.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLog(LogType.Error, "PLC", "Recipe", "Recipe could not be written to the PLC: " + ex.Message, ex.ToString());
                        done.SetException(ex);
                    }
                }, TaskScheduler.Default);
            }

            return done.Task;
        }
    }
}
