using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// <see cref="IPlcRecipeService"/> over the PLC's recipe block, FB_RecipeMapping
    /// (App.config PlcRecipeSymbol, e.g. MAIN.recipe.Recipe): one FB_Recipe per gantry
    /// (E_Recipe), which the PLC sequences drive every taught move from
    /// (FUN_AuxServoMoveToPos). The HMI's recipe table is the PLC's table: a row is a
    /// position, a column group (X, XVel, XAcc, XDeAcc ...) an axis.
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
    /// the gantry, so a table's X columns are always axis 1 whichever gantry it is. An axis
    /// the table has no columns for, between two it has, is sent as zeros - speed 0, which
    /// the PLC takes as "not taught". The module's gantry is its PlcGantry in
    /// GantryStructure.json; a module without one is not written.
    ///
    /// bLoaded is cleared first, so a sequence never drives from a half-written recipe, then
    /// Step and Name go over in one write each, then nPosUsed and nAxisUsed, then bLoaded.
    /// The rows and axes the table does not use are zeroed, so nothing of a longer recipe
    /// written before is left behind. MAX_POS and MAX_AXES are taken from the sizes the PLC
    /// declares Name and Step with, so a bigger PLC table needs no change here.
    ///
    /// The PLC does not keep the recipe over a restart, so every mapped gantry is written
    /// again each time the PLC comes back to Run; the Recipe page writes a gantry on Save.
    /// The writes are made one at a time, in order, on a worker thread.
    /// </remarks>
    public sealed class TwincatRecipeService : IPlcRecipeService
    {
        private const int AxisMoveSize = 4 * sizeof(double);    // ST_AxisMove
        private const int NameLength = 30;                       // STRING(30)
        private const int NameSize = NameLength + 1;             // and its terminating 0

        /// <summary>The axes in E_Servo order: X_AXIS = 1 ... R_AXIS = 4.</summary>
        private static readonly string[] AxisColumns = { "X", "Y", "Z", "R" };

        private static readonly Encoding PlcText = Encoding.GetEncoding(1252);

        private readonly TwincatPlcConnection _plc;
        private readonly PlcSubscriptions _subscriptions;
        private readonly IRecipeService _recipes;
        private readonly string _recipeSymbol;
        private readonly object _sync = new object();

        private Task _writes = Task.CompletedTask;   // the PLC writes waiting to be made, in order
        private bool _wasLive;

        public TwincatRecipeService(
            TwincatPlcConnection plc, PlcSubscriptions subscriptions, IRecipeService recipes, string recipeSymbol)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            _recipeSymbol = (recipeSymbol ?? string.Empty).Trim();

            if (_recipeSymbol.Length > 0)
                _subscriptions.LiveChanged += OnLiveChanged;
        }

        public bool IsAvailable => _recipeSymbol.Length > 0;

        public string UnavailableReason => IsAvailable ? string.Empty : "PlcRecipeSymbol is not set in App.config.";

        /// <summary>Writes every mapped gantry if the PLC is already live; from then on, each time it comes back.</summary>
        public void Start()
        {
            OnLiveChanged(this, EventArgs.Empty);
        }

        public Task WriteGantryRecipeAsync(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
                return Task.FromException(new ArgumentException("Module name is required.", nameof(moduleName)));

            return QueueWrite(() => WriteGantry(moduleName));
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

            QueueWrite(WriteAllGantries);
        }

        private void WriteAllGantries()
        {
            foreach (string module in _recipes.GetGantryModules())
            {
                if (_recipes.GetGantryModuleDefinition(module).PlcGantry < 1)
                {
                    Logger.WriteLog(LogType.Info, "PLC", "Recipe",
                        module + " has no PlcGantry in GantryStructure.json - its recipe is not written to the PLC.", string.Empty);
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
                throw new InvalidOperationException(moduleName + " is not mapped to a PLC gantry. Set its \"PlcGantry\" (the PLC's E_Recipe number, 1-8) in Data\\GantryStructure.json.");

            if (!_plc.IsConnected)
                throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");

            string recipe = _recipeSymbol + "[" + gantry + "]";
            int maxPositions;
            int maxAxes;
            ReadTableSize(recipe, out maxPositions, out maxAxes);

            DynamicTableDocument table = _recipes.LoadGantryRecipeDynamic(moduleName);
            IReadOnlyList<Dictionary<string, string>> rows = table?.Rows ?? new Dictionary<string, string>[0];
            var columns = new HashSet<string>(
                (table?.Columns ?? new DynamicColumnSchema[0]).Select(c => c.Key), StringComparer.OrdinalIgnoreCase);

            // nAxisUsed: up to the last axis the table has columns for.
            int axesUsed = 0;
            for (int axis = 1; axis <= AxisColumns.Length; axis++)
            {
                if (columns.Contains(AxisColumns[axis - 1]))
                    axesUsed = axis;
            }

            if (rows.Count == 0)
                throw new InvalidOperationException("the " + moduleName + " recipe has no positions.");
            if (axesUsed == 0)
                throw new InvalidOperationException("the " + moduleName + " recipe has no X, Y, Z or R column.");
            if (rows.Count > maxPositions)
                throw new InvalidOperationException("the " + moduleName + " recipe has " + rows.Count + " positions; the PLC holds " + maxPositions + " (MAX_POS).");
            if (axesUsed > maxAxes)
                throw new InvalidOperationException("the " + moduleName + " recipe has " + axesUsed + " axes; the PLC holds " + maxAxes + " (MAX_AXES).");

            var step = new byte[maxPositions * maxAxes * AxisMoveSize];
            var names = new byte[maxPositions * NameSize];
            for (int row = 0; row < rows.Count; row++)
            {
                string name = Cell(rows[row], "Position");
                PutName(names, row * NameSize, name);

                for (int axis = 1; axis <= axesUsed; axis++)
                {
                    string column = AxisColumns[axis - 1];
                    if (!columns.Contains(column))
                        continue;   // sent as zeros: not taught

                    int offset = (row * maxAxes + (axis - 1)) * AxisMoveSize;   // Step[row, axis], the axis index the faster
                    PutReal(step, offset, Number(moduleName, name, rows[row], column));
                    PutReal(step, offset + 8, Number(moduleName, name, rows[row], column + "Vel"));
                    PutReal(step, offset + 16, Number(moduleName, name, rows[row], column + "Acc"));
                    PutReal(step, offset + 24, Number(moduleName, name, rows[row], column + "DeAcc"));
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
                throw new InvalidOperationException(recipe + " is not in the PLC (no Name or Step) - check PlcRecipeSymbol and the gantry number.");

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

        private static string Cell(Dictionary<string, string> row, string key)
        {
            string value;
            return row != null && row.TryGetValue(key, out value) && value != null ? value.Trim() : string.Empty;
        }

        /// <summary>The cell as a number; a blank cell is 0. Throws for anything else that is not a number.</summary>
        private static double Number(string moduleName, string position, Dictionary<string, string> row, string key)
        {
            string raw = Cell(row, key);
            if (raw.Length == 0)
                return 0d;

            double value;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return value;
            }

            throw new InvalidOperationException("the " + moduleName + " recipe's '" + position + "' " + key + " is '" + raw + "', not a number.");
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
            if (!IsAvailable)
                return Task.FromException(new InvalidOperationException(UnavailableReason));

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
