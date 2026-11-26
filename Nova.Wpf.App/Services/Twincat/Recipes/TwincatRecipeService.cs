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
    /// (FUN_AuxServoMoveToPos). A gantry's recipe is its file in the data folder,
    /// {Name}.xlsx or {Name}.csv (Name and PlcGantry from Data\Gantry.json): a group of four
    /// columns per servo (Value, Speed, Acceleration, Deceleration) and a row per position.
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
    /// The axis number is E_Servo, local to the gantry (UC_Loading_Servo_X_Axis = 1,
    /// UC_Loading_Servo_Z_Axis = 2, and the same for every gantry): the recipe file's first
    /// column group is axis 1, the second axis 2, and so on, so the file's servo order must
    /// be E_Servo's. The row number is the position's row in the file (row 3 = 1), which is
    /// the sequences' E_Servo_Pos_Loading / E_Servo_Pos_Unloading (Safe_Pos = 1, Pick_Pos = 2,
    /// Place_Pos_Station_1 = 3, Place_Pos_Station_2 = 4), so the file's row order must be
    /// theirs. Position p of axis a is Step[p, a], Name[p] the position's name, nPosUsed the
    /// number of positions and nAxisUsed the number of axes; every other cell is zero (speed
    /// 0, which the PLC takes as "not taught"). rPos,
    /// rVel, rAcc and rDec are the Value, Speed, Acceleration and Deceleration. A gantry
    /// without a PlcGantry is not written, and one whose file names a servo another gantry's
    /// file names too is refused.
    ///
    /// A table is written whole (<see cref="WriteTable"/>), the same way for a gantry and a
    /// CNC channel, under the PLC lock: bLoaded is cleared first, so a sequence never drives
    /// from a half-written recipe, then Step and Name go over in one write each, then nPosUsed
    /// and nAxisUsed. All four are then read back and compared byte for byte with what was
    /// sent; only when they match is bLoaded set TRUE. A table that reads back differently is
    /// written once more, and if it still differs bLoaded is left FALSE and the write fails,
    /// naming the first cell that differs (Step[row, axis].member, read and sent) and how many
    /// do. The rows and axes the table does not use are zeroed, so nothing of a longer recipe
    /// written before is left behind. MAX_POS and MAX_AXES are taken from the sizes the PLC
    /// declares Name and Step with, so a bigger PLC table needs no change here.
    ///
    /// A few seconds after each successful write the table is read once more and compared
    /// with the last thing the HMI sent to it (<see cref="Recheck"/>): one read, not a poll.
    /// A difference, or bLoaded FALSE, is logged as an error - something in the PLC changed
    /// or re-initialised the table after the HMI wrote it - and the table is not written
    /// again, so the log shows the PLC's doing rather than hiding it.
    ///
    /// Each FB_Recipe belongs to one recipe: a gantry module (PlcGantry) or a CNC channel
    /// (Cnc.Channels.n.RecipeGantry). A table that two of them map to is never written: they
    /// would overwrite each other, the last writer winning.
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
    /// taught" and the feed itself is in the CNC program. So a CNC cell reads
    /// (position, TaughtVelocity, TaughtVelocity, TaughtVelocity) - 0, 1, 1, 1 for a position
    /// of 0 - and the columns past the channel's last axis number are zeros.
    ///
    /// The PLC does not keep the recipe over a restart, so every mapped gantry, the robot
    /// values and every mapped CNC channel are written again each time the PLC comes back to
    /// Run; one that fails then is tried again twice, 5 s apart, while the PLC stays in Run.
    /// The Recipe page writes a gantry, the robot values or a CNC channel on Save. The writes
    /// are made one at a time, in order, on a worker thread.
    /// </remarks>
    public sealed class TwincatRecipeService : IPlcRecipeService
    {
        private const int AxisMoveSize = 4 * sizeof(double);    // ST_AxisMove
        private const int NameLength = 30;                       // STRING(30)
        private const int NameSize = NameLength + 1;             // and its terminating 0

        private const int TableAttempts = 2;                     // a table that reads back differently is written once more
        private const int StartupAttempts = 3;                   // the writes made when the PLC comes back
        private static readonly TimeSpan StartupRetryDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RecheckDelay = TimeSpan.FromSeconds(3);

        /// <summary>ST_AxisMove's members, in order.</summary>
        private static readonly string[] MoveMembers = { "rPos", "rVel", "rAcc", "rDec" };

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
        private int _liveGeneration;                 // counts the PLC's returns to Run; a retry from an earlier one is dropped

        /// <summary>The last table written to each FB_Recipe, by its symbol: what <see cref="Recheck"/> compares with.</summary>
        private readonly Dictionary<string, RecipeTable> _sent = new Dictionary<string, RecipeTable>(StringComparer.OrdinalIgnoreCase);

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

            return QueueWrite(() => WriteTable(GantryTable(moduleName)));
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

            return QueueWrite(() => WriteTable(CncTable(CncChannel(channelName))));
        }

        // ==================================================================
        // When the PLC comes back to Run
        // ==================================================================

        /// <summary>One recipe written when the PLC comes back, named for the log.</summary>
        private sealed class RecipeJob
        {
            public string Name;
            public Action Write;
        }

        /// <summary>Raised on the ADS or a worker thread: only queues the writes.</summary>
        private void OnLiveChanged(object sender, EventArgs e)
        {
            int generation;
            lock (_sync)
            {
                bool live = _subscriptions.IsLive;
                bool cameBack = live && !_wasLive;
                _wasLive = live;
                if (!cameBack)
                    return;

                generation = ++_liveGeneration;
            }

            QueueWrite(() => RunJobs(StartupJobs(), generation, 1));
        }

        /// <summary>Everything the PLC loses over a restart: every mapped gantry, the robot values, every mapped CNC channel.</summary>
        private List<RecipeJob> StartupJobs()
        {
            var jobs = new List<RecipeJob>();

            if (IsAvailable)
            {
                foreach (string module in _recipes.GetGantryModules())
                {
                    if (_recipes.GetGantryModuleDefinition(module).PlcGantry < 1)
                    {
                        Logger.WriteLog(LogType.Info, "PLC", "Recipe",
                            module + " has no PlcGantry in Gantry.json - its recipe is not written to the PLC.", string.Empty);
                        continue;
                    }

                    string name = module;
                    jobs.Add(new RecipeJob { Name = name + " recipe", Write = () => WriteTable(GantryTable(name)) });
                }
            }

            if (_robotSymbol.Length > 0)
                jobs.Add(new RecipeJob { Name = "robot recipe", Write = WriteRobotValues });

            if (HasCncRecipes)
            {
                IReadOnlyList<CncChannelRecipe> channels = null;
                try
                {
                    channels = _recipes.LoadCncRecipe();
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "PLC", "Recipe",
                        "The CNC recipe could not be read: " + ex.Message, ex.ToString());
                }

                foreach (CncChannelRecipe channel in channels ?? new CncChannelRecipe[0])
                {
                    if (channel.PlcChannel < 1)
                    {
                        Logger.WriteLog(LogType.Info, "PLC", "Recipe",
                            channel.Name + " has no PlcChannel in CncRecipe.json - its positions are not written to the PLC.", string.Empty);
                        continue;
                    }

                    string name = channel.Name;
                    jobs.Add(new RecipeJob { Name = name + " CNC recipe", Write = () => WriteTable(CncTable(CncChannel(name))) });
                }
            }

            return jobs;
        }

        /// <summary>
        /// Runs the jobs, each on its own. Those that fail are tried again after
        /// <see cref="StartupRetryDelay"/>, up to <see cref="StartupAttempts"/> times in all, as
        /// long as the PLC is still in the Run it came back to - a later return writes
        /// everything anyway. Each attempt reads the files again.
        /// </summary>
        private void RunJobs(List<RecipeJob> jobs, int generation, int attempt)
        {
            var failed = new List<RecipeJob>();
            foreach (RecipeJob job in jobs)
            {
                try
                {
                    job.Write();
                }
                catch (Exception ex)
                {
                    failed.Add(job);
                    string next = attempt < StartupAttempts
                        ? " (attempt " + attempt + " of " + StartupAttempts + "; tried again in " + StartupRetryDelay.TotalSeconds + " s)"
                        : " (attempt " + attempt + " of " + StartupAttempts + "; written again the next time the PLC comes back to Run, or on Save)";
                    Logger.WriteLog(LogType.Error, "PLC", "Recipe",
                        "The " + job.Name + " could not be written to the PLC" + next + ": " + ex.Message, ex.ToString());
                }
            }

            if (failed.Count == 0 || attempt >= StartupAttempts)
                return;

            Task.Delay(StartupRetryDelay).ContinueWith(_ =>
            {
                lock (_sync)
                {
                    if (generation != _liveGeneration || !_subscriptions.IsLive)
                        return;
                }

                QueueWrite(() => RunJobs(failed, generation, attempt + 1));
            }, TaskScheduler.Default);
        }

        // ==================================================================
        // A recipe table (FB_Recipe), gantry or CNC
        // ==================================================================

        /// <summary>One FB_Recipe's contents as they are sent, laid out as the PLC lays them out.</summary>
        private sealed class RecipeTable
        {
            public string Description;          // "the UCLoadingGantry recipe", for the log and errors
            public int Index;                   // n of Recipe[n]
            public string Symbol;               // Recipe[n]
            public int MaxAxes;                 // MAX_AXES, the second dimension of Step
            public string[] AxisLabels;         // per column, null where unused
            public List<string> RowNames;       // per row used
            public byte[] Step;                 // MAX_POS x MAX_AXES x ST_AxisMove
            public byte[] Names;                // MAX_POS x STRING(30)
            public short PosUsed;
            public short AxisUsed;

            public string RowLabel(int row)
            {
                return row < RowNames.Count ? "'" + RowNames[row] + "'" : "unused row";
            }

            public string AxisLabel(int axis)
            {
                return axis < AxisLabels.Length && AxisLabels[axis] != null ? AxisLabels[axis] : "unused axis";
            }
        }

        /// <summary>The gantry module's recipe table, from Gantry.json and Servos.json as they are now.</summary>
        private RecipeTable GantryTable(string moduleName)
        {
            GantryModuleDefinition definition = _recipes.GetGantryModuleDefinition(moduleName);
            int gantry = definition.PlcGantry;
            if (gantry < 1)
                throw new InvalidOperationException(moduleName + " is not mapped to a PLC gantry. Set its \"PlcGantry\" (the PLC's E_Recipe number, 1-8) in Data\\Gantry.json.");

            if (!_plc.IsConnected)
                throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");

            // The recipe file's column groups, in order, are the gantry's axes 1..N (E_Servo).
            List<string> axes = definition.Axes.Select(a => a.Name).ToList();
            IReadOnlyList<GantryPosition> positions = _recipes.LoadGantryPositions(moduleName);
            int axisCount = axes.Count;

            if (positions.Count == 0)
                throw new InvalidOperationException("the " + moduleName + " recipe file has no positions (column A from row 3 is empty).");
            if (axisCount == 0)
                throw new InvalidOperationException("the " + moduleName + " recipe file names no servo in row 1.");
            CheckServosNotShared(moduleName, axes);

            string recipe = _recipeSymbol + "[" + gantry + "]";
            int maxPositions;
            int maxAxes;
            ReadTableSize(recipe, out maxPositions, out maxAxes);
            return GantryLayout(moduleName, gantry, recipe, axes, positions, maxPositions, maxAxes);
        }

        /// <summary>The gantry's table as the PLC lays it out (see the class remarks); no PLC access.</summary>
        private static RecipeTable GantryLayout(string moduleName, int gantry, string recipe, List<string> axes,
            IReadOnlyList<GantryPosition> positions, int maxPositions, int maxAxes)
        {
            int axisCount = axes.Count;
            if (axisCount > maxAxes)
                throw new InvalidOperationException("the " + moduleName + " recipe has " + axisCount + " axes; the PLC holds " + maxAxes + " (MAX_AXES).");
            if (positions.Count > maxPositions)
                throw new InvalidOperationException("the " + moduleName + " recipe has " + positions.Count + " positions; the PLC holds " + maxPositions + " (MAX_POS).");

            var table = NewTable("the " + moduleName + " recipe", gantry, recipe, maxPositions, maxAxes, (short)positions.Count, (short)axisCount);
            for (int a = 0; a < axisCount; a++)
                table.AxisLabels[a] = "axis " + (a + 1) + " " + axes[a];

            // Row = position, as E_Servo_Pos_* numbers them (Safe_Pos = 1, Pick_Pos = 2, ...);
            // column = axis, as E_Servo numbers them (X = 1, Z = 2): Step[p, a].
            for (int p = 0; p < positions.Count; p++)
            {
                string name = positions[p].Name ?? string.Empty;
                table.RowNames.Add(name);
                PutName(table.Names, p * NameSize, name);

                for (int a = 0; a < axisCount; a++)
                {
                    GantryAxisMove move;
                    if (positions[p].Axes == null || !positions[p].Axes.TryGetValue(axes[a], out move) || move == null)
                        continue;   // sent as zeros: not taught

                    int offset = (p * maxAxes + a) * AxisMoveSize;   // Step[row, axis], the axis index the faster
                    PutReal(table.Step, offset, move.Value);
                    PutReal(table.Step, offset + 8, move.Speed);
                    PutReal(table.Step, offset + 16, move.Acceleration);
                    PutReal(table.Step, offset + 24, move.Deceleration);
                }
            }

            return table;
        }

        /// <summary>
        /// Refuses a gantry whose recipe file names a servo another gantry's file names too: one
        /// of the two files is wrong, and the PLC would move that servo from both recipes.
        /// </summary>
        private void CheckServosNotShared(string moduleName, IReadOnlyList<string> axes)
        {
            foreach (string other in _recipes.GetGantryModules())
            {
                if (string.Equals(other.Trim(), moduleName.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                List<string> shared = _recipes.GetGantryModuleDefinition(other).Axes
                    .Select(a => a.Name)
                    .Where(name => axes.Any(axis => string.Equals(axis, name, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (shared.Count > 0)
                {
                    throw new InvalidOperationException("the " + moduleName + " recipe names " + string.Join(", ", shared) + ", which the "
                        + other + " recipe names too. Correct the recipe file that names the wrong servo; neither is written to the PLC until then.");
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

        /// <summary>The CNC channel's recipe table, from CncRecipe.json and the symbol map.</summary>
        private RecipeTable CncTable(CncChannelRecipe channel)
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

            var table = NewTable("the " + channel.Name + " CNC recipe", map.RecipeGantry, recipe, maxPositions, maxAxes, (short)rows.Count, (short)axesUsed);
            foreach (var column in columns)
                table.AxisLabels[column.Value - 1] = column.Key;

            double taught = _cnc.TaughtVelocity > 0 ? _cnc.TaughtVelocity : 1.0;
            for (int row = 0; row < rows.Count; row++)
            {
                table.RowNames.Add(rows[row].Name);
                PutName(table.Names, row * NameSize, rows[row].Name);
                foreach (var column in columns)
                {
                    double value;
                    if (rows[row].Values == null || !rows[row].Values.TryGetValue(column.Key, out value))
                        continue;   // sent as zeros: not taught

                    int offset = (row * maxAxes + (column.Value - 1)) * AxisMoveSize;   // Step[row, axis]
                    PutReal(table.Step, offset, value);
                    PutReal(table.Step, offset + 8, taught);
                    PutReal(table.Step, offset + 16, taught);
                    PutReal(table.Step, offset + 24, taught);
                }
            }

            return table;
        }

        private static RecipeTable NewTable(string description, int index, string symbol, int maxPositions, int maxAxes, short posUsed, short axisUsed)
        {
            return new RecipeTable
            {
                Description = description,
                Index = index,
                Symbol = symbol,
                MaxAxes = maxAxes,
                AxisLabels = new string[maxAxes],
                RowNames = new List<string>(),
                Step = new byte[maxPositions * maxAxes * AxisMoveSize],
                Names = new byte[maxPositions * NameSize],
                PosUsed = posUsed,
                AxisUsed = axisUsed,
            };
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

        /// <summary>
        /// Writes the table, reads it back and sets bLoaded only when what the PLC holds is what
        /// was sent; a table that reads back differently is written once more. All under the
        /// PLC lock, so no other HMI call comes between. Throws, with bLoaded left FALSE, when
        /// it still differs.
        /// </summary>
        private void WriteTable(RecipeTable table)
        {
            CheckSoleOwner(table);

            // Until this write has been read back, the table holds nothing the HMI vouches
            // for: a check queued by an earlier write must not compare with it.
            lock (_sync)
            {
                _sent.Remove(table.Symbol);
            }

            string difference = null;
            string firstDifference = null;   // logged after the lock, as nothing is logged under it
            int attempt;
            lock (_plc.SyncRoot)
            {
                for (attempt = 1; attempt <= TableAttempts; attempt++)
                {
                    _plc.WriteSymbol(table.Symbol + ".bLoaded", false);
                    _plc.WriteBytes(table.Symbol + ".Step", table.Step);
                    _plc.WriteBytes(table.Symbol + ".Name", table.Names);
                    _plc.WriteSymbol(table.Symbol + ".nPosUsed", table.PosUsed);
                    _plc.WriteSymbol(table.Symbol + ".nAxisUsed", table.AxisUsed);

                    difference = Difference(table);
                    if (difference == null)
                    {
                        _plc.WriteSymbol(table.Symbol + ".bLoaded", true);
                        break;
                    }

                    if (firstDifference == null)
                        firstDifference = difference;
                }
            }

            if (firstDifference != null)
            {
                Logger.WriteLog(LogType.Error, "PLC", "Recipe",
                    table.Symbol + " (" + table.Description + ") read back differently from what was just written: "
                    + firstDifference + ". It was written again.", string.Empty);
            }

            if (difference != null)
            {
                throw new InvalidOperationException(table.Symbol + " (" + table.Description + ") read back differently from what was written, "
                    + TableAttempts + " times: " + difference + ". bLoaded was left FALSE, so the PLC sequences do not drive from it.");
            }

            lock (_sync)
            {
                _sent[table.Symbol] = table;
            }

            Logger.WriteLog(LogType.Info, "PLC", "Recipe",
                table.Symbol + " holds " + table.Description + " (" + table.PosUsed + " positions, " + table.AxisUsed + " axes): written and read back unchanged"
                + (attempt > 1 ? " at the second attempt." : "."), string.Empty);

            ScheduleRecheck(table.Symbol);
        }

        /// <summary>
        /// Refuses a table that another recipe maps to as well - a gantry module and a CNC
        /// channel, or two of either - since each would overwrite the other.
        /// </summary>
        private void CheckSoleOwner(RecipeTable table)
        {
            var owners = new List<string>();
            foreach (string module in _recipes.GetGantryModules())
            {
                if (_recipes.GetGantryModuleDefinition(module).PlcGantry == table.Index)
                    owners.Add("gantry module " + module + " (PlcGantry in Data\\Gantry.json)");
            }

            if (HasCncRecipes)
            {
                IReadOnlyList<CncChannelRecipe> channels;
                try
                {
                    channels = _recipes.LoadCncRecipe();
                }
                catch (Exception)
                {
                    channels = new CncChannelRecipe[0];   // the CNC write itself reports an unreadable file
                }

                foreach (CncChannelRecipe channel in channels)
                {
                    CncChannelSymbols map = channel.PlcChannel >= 1 ? _cnc.ChannelMap(channel.PlcChannel) : null;
                    if (map != null && map.RecipeGantry == table.Index)
                        owners.Add("CNC " + channel.Name + " (Cnc.Channels." + channel.PlcChannel + ".RecipeGantry in Data\\" + PlcSymbolMap.FileName + ")");
                }
            }

            if (owners.Count > 1)
            {
                throw new InvalidOperationException(table.Symbol + " is the recipe table of " + string.Join(" and of ", owners)
                    + ": they would overwrite each other. Give each its own table.");
            }
        }

        /// <summary>
        /// What the PLC holds against what the table sent, or null when they are the same.
        /// Names the first Step cell that differs (with its member, read and sent value) and
        /// how many do, the first Name, and nPosUsed / nAxisUsed.
        /// </summary>
        private string Difference(RecipeTable table)
        {
            byte[] step = _plc.ReadBytes(table.Symbol + ".Step", table.Step.Length);
            byte[] names = _plc.ReadBytes(table.Symbol + ".Name", table.Names.Length);
            long posUsed = Convert.ToInt64(_plc.ReadSymbol(table.Symbol + ".nPosUsed"), CultureInfo.InvariantCulture);
            long axisUsed = Convert.ToInt64(_plc.ReadSymbol(table.Symbol + ".nAxisUsed"), CultureInfo.InvariantCulture);

            var differences = new List<string>();
            string stepDifference = StepDifference(table, step);
            if (stepDifference != null)
                differences.Add(stepDifference);

            string nameDifference = NameDifference(table, names);
            if (nameDifference != null)
                differences.Add(nameDifference);

            if (posUsed != table.PosUsed)
                differences.Add("nPosUsed reads " + posUsed + ", sent " + table.PosUsed);
            if (axisUsed != table.AxisUsed)
                differences.Add("nAxisUsed reads " + axisUsed + ", sent " + table.AxisUsed);

            return differences.Count == 0 ? null : string.Join("; ", differences);
        }

        private static string StepDifference(RecipeTable table, byte[] read)
        {
            int cells = table.Step.Length / AxisMoveSize;
            int differing = 0;
            string first = null;
            for (int cell = 0; cell < cells; cell++)
            {
                int offset = cell * AxisMoveSize;
                if (SameBytes(table.Step, read, offset, AxisMoveSize))
                    continue;

                differing++;
                if (first != null)
                    continue;

                int member = 0;
                while (member < MoveMembers.Length - 1 && SameBytes(table.Step, read, offset + member * sizeof(double), sizeof(double)))
                    member++;

                int at = offset + member * sizeof(double);
                int row = cell / table.MaxAxes;
                int axis = cell % table.MaxAxes;
                first = "Step[" + (row + 1) + ", " + (axis + 1) + "]." + MoveMembers[member]
                    + " (" + table.RowLabel(row) + ", " + table.AxisLabel(axis) + ") reads " + Number(BitConverter.ToDouble(read, at))
                    + ", sent " + Number(BitConverter.ToDouble(table.Step, at));
            }

            return differing == 0 ? null : first + " - " + differing + " of " + cells + " Step cells differ";
        }

        private static string NameDifference(RecipeTable table, byte[] read)
        {
            int rows = table.Names.Length / NameSize;
            int differing = 0;
            string first = null;
            for (int row = 0; row < rows; row++)
            {
                int offset = row * NameSize;
                if (SameBytes(table.Names, read, offset, NameSize))
                    continue;

                differing++;
                if (first == null)
                    first = "Name[" + (row + 1) + "] reads '" + Text(read, offset) + "', sent '" + Text(table.Names, offset) + "'";
            }

            return differing == 0 ? null : first + " - " + differing + " of " + rows + " names differ";
        }

        private static bool SameBytes(byte[] a, byte[] b, int offset, int count)
        {
            for (int i = offset; i < offset + count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        private static string Text(byte[] buffer, int offset)
        {
            int length = 0;
            while (length < NameLength && buffer[offset + length] != 0)
                length++;

            return PlcText.GetString(buffer, offset, length);
        }

        private static string Number(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Queues <see cref="Recheck"/> after <see cref="RecheckDelay"/>. Dropped if the PLC has
        /// left Run since the write: a table a restart re-initialised is not the PLC changing it,
        /// and the return to Run writes it again anyway.
        /// </summary>
        private void ScheduleRecheck(string symbol)
        {
            int generation;
            lock (_sync)
            {
                generation = _liveGeneration;
            }

            Task.Delay(RecheckDelay).ContinueWith(_ =>
            {
                lock (_sync)
                {
                    if (generation != _liveGeneration || !_subscriptions.IsLive)
                        return;
                }

                QueueWrite(() => Recheck(symbol, generation));
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// Reads the table once, a few seconds after it was written, and compares it with the
        /// last thing the HMI sent to it. Only logs: a table the PLC changed is not written
        /// again, so the PLC's doing shows in the log instead of being covered up.
        /// </summary>
        private void Recheck(string symbol, int generation)
        {
            RecipeTable table;
            lock (_sync)
            {
                if (generation != _liveGeneration || !_subscriptions.IsLive || !_sent.TryGetValue(symbol, out table))
                    return;
            }

            try
            {
                if (!_plc.IsConnected)
                    return;   // the PLC writes everything again when it comes back

                string difference;
                bool loaded;
                lock (_plc.SyncRoot)
                {
                    difference = Difference(table);
                    loaded = Convert.ToBoolean(_plc.ReadSymbol(symbol + ".bLoaded"), CultureInfo.InvariantCulture);
                }

                if (!loaded)
                    difference = (difference == null ? string.Empty : difference + "; ") + "bLoaded is FALSE";

                if (difference == null)
                {
                    Logger.WriteLog(LogType.Info, "PLC", "Recipe",
                        symbol + " (" + table.Description + ") still holds what was written " + RecheckDelay.TotalSeconds + " s after.", string.Empty);
                    return;
                }

                Logger.WriteLog(LogType.Error, "PLC", "Recipe",
                    symbol + " (" + table.Description + ") read back unchanged when it was written, but within " + RecheckDelay.TotalSeconds
                    + " s the PLC holds something else: " + difference + ". Something in the PLC changes or re-initialises the recipe table; "
                    + "the HMI did not write it again.", string.Empty);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "PLC", "Recipe",
                    symbol + " (" + table.Description + ") could not be read back for its check: " + ex.Message, ex.ToString());
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
        // Helpers
        // ==================================================================

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
