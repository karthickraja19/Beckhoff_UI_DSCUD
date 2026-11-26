using System.Collections.Generic;

namespace Master.Configuration.Model
{
    /// <summary>
    /// One gantry of Gantry.json: its Name and the PLC gantry its recipe goes to. Its recipe
    /// is the file {Name}.xlsx (or {Name}.csv) in the data folder, whose column groups name
    /// its servos. Axes is optional: the Type (jog button) and move order of the servos it lists.
    /// </summary>
    public class GantryConfiguration
    {
        public string Name { get; set; }

        /// <summary>
        /// The PLC gantry (E_Recipe, 1-based) whose FB_Recipe this module's positions are
        /// written to; 0 when the module is not mapped to one, and nothing is written.
        /// </summary>
        public int PlcGantry { get; set; }

        public List<_Axis> Axes { get; set; }
    }

    /// <summary>One axis of a gantry module.</summary>
    public class _Axis
    {
        /// <summary>The servo's EnumName in Servos.json.</summary>
        public string Name { get; set; }

        /// <summary>X, Y, Z or R: the jog button that drives it. (Its axis number in the PLC's recipe is its column group in the recipe file.)</summary>
        public string Type { get; set; }

        /// <summary>Order the axis moves in when a position is moved to (1 first; 0 = after the numbered ones).</summary>
        public int MovementPriority { get; set; }

        /// <summary>The same for a position whose name contains "safe".</summary>
        public int SafePositionPriority { get; set; }
    }
}
