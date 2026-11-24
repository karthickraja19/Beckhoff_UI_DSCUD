using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Data.Models
{
    /// <summary>
    /// A named position of a gantry module, as V3 keeps it: the position taught on each of
    /// the module's servos (Servos.json ConfiguredPositions with that name).
    /// </summary>
    public sealed class GantryPosition
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>The move per axis, keyed by the servo's EnumName. A servo without this position is absent.</summary>
        public Dictionary<string, GantryAxisMove> Axes { get; set; } = new Dictionary<string, GantryAxisMove>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>One servo's part of a gantry position.</summary>
    public sealed class GantryAxisMove
    {
        public double Value { get; set; }
        public double Speed { get; set; }
        public double Acceleration { get; set; }
        public double Deceleration { get; set; }
    }

    /// <summary>The column keys of the Gantry tab's table, as V3 names them: one group of four per servo.</summary>
    public static class GantryRecipeColumns
    {
        public const string Position = "Position";

        public static string Value(string servo) => servo + "_Value";
        public static string Speed(string servo) => servo + "_Speed";
        public static string Acc(string servo) => servo + "_Acc";
        public static string Dec(string servo) => servo + "_Dec";
    }

    /// <summary>One axis of a gantry module (Gantry.json).</summary>
    public sealed class GantryAxisDefinition
    {
        /// <summary>The servo's EnumName, which is also its axis ComponentId.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>"X", "Y", "Z" or "R".</summary>
        public string Type { get; set; } = string.Empty;

        public int MovementPriority { get; set; }
        public int SafePositionPriority { get; set; }
    }

    public sealed class RobotPointRecord
    {
        public string PointName { get; set; } = string.Empty;
        public string Value { get; set; } = "0";
    }

    /// <summary>
    /// One CNC channel of the Recipe page's CNC tab (Data\CncRecipe.json): which servo
    /// drives each of its axes, and its named positions, each taught on every axis.
    /// </summary>
    public sealed class CncChannelRecipe
    {
        /// <summary>
        /// The channel's axes, in the PLC's order: X1, Y1, Z1 linear, A, R rotational, then
        /// the second linear pair X2, Z2.
        /// </summary>
        public static readonly IReadOnlyList<string> AxisNames = new[] { "X1", "Y1", "Z1", "A", "R", "X2", "Z2" };

        /// <summary>True for A and R (degrees); every other axis is linear (mm).</summary>
        public static bool IsRotational(string axis) => axis == "A" || axis == "R";

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The PLC's CNC recipe (1-based) this channel's positions are written to; 0 when the
        /// channel is not mapped to one, and its positions are not written to the PLC.
        /// </summary>
        public int PlcChannel { get; set; }

        public List<CncAxisRecipe> Axes { get; set; } = new List<CncAxisRecipe>();

        /// <summary>The channel's positions, in the order the PLC numbers them (row 1 first).</summary>
        public List<CncPositionRecipe> Positions { get; set; } = new List<CncPositionRecipe>();
    }

    public sealed class CncAxisRecipe
    {
        /// <summary>One of <see cref="CncChannelRecipe.AxisNames"/>.</summary>
        public string Axis { get; set; } = string.Empty;

        /// <summary>The servo's EnumName in Data\Servos.json; blank when the axis is not mapped.</summary>
        public string Servo { get; set; } = string.Empty;
    }

    /// <summary>A named position of a CNC channel: its name is kept from the JSON, its values taught or typed.</summary>
    public sealed class CncPositionRecipe
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>The taught value per axis name: mm, degrees for A and R. An axis left out is 0.</summary>
        public Dictionary<string, double> Values { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A gantry module of Gantry.json: its axes, and the PLC gantry its recipe goes to.</summary>
    public sealed class GantryModuleDefinition
    {
        public string ModuleName { get; set; } = string.Empty;

        /// <summary>The module's axes as Gantry.json lists them (one per type; the table's column groups in this order).</summary>
        public IReadOnlyList<GantryAxisDefinition> Axes { get; set; } = Array.Empty<GantryAxisDefinition>();

        /// <summary>The axes' names in the order they move to a position (MovementPriority; 0 last).</summary>
        public IReadOnlyList<string> MovementPriority { get; set; } = Array.Empty<string>();

        /// <summary>The same for a position whose name contains "safe" (SafePositionPriority).</summary>
        public IReadOnlyList<string> SafePositionPriority { get; set; } = Array.Empty<string>();

        /// <summary>
        /// The PLC gantry (E_Recipe, 1-based) whose recipe this module's table is written to;
        /// 0 when the module is not mapped to one, and its recipe is not written to the PLC.
        /// </summary>
        public int PlcGantry { get; set; }
    }

    public enum RecipeSaveStatus
    {
        Saved = 0,
        NoChanges = 1,
    }

    public sealed class RecipeSaveResult
    {
        public RecipeSaveStatus Status { get; }
        public string Message { get; }

        public bool IsSaved => Status == RecipeSaveStatus.Saved;
        public bool IsNoChanges => Status == RecipeSaveStatus.NoChanges;

        private RecipeSaveResult(RecipeSaveStatus status, string message)
        {
            Status = status;
            Message = message;
        }

        public static RecipeSaveResult Saved(string message)
        {
            return new RecipeSaveResult(RecipeSaveStatus.Saved, message);
        }

        public static RecipeSaveResult NoChanges(string message)
        {
            return new RecipeSaveResult(RecipeSaveStatus.NoChanges, message);
        }
    }
}
