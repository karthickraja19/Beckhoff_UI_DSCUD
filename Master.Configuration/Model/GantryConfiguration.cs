using System.Collections.Generic;

namespace Master.Configuration.Model
{
    /// <summary>
    /// One gantry module of Gantry.json (as in Odyssey.Master_V3): the servos it is made of.
    /// Its recipe is the positions taught on those servos - each servo's ConfiguredPositions
    /// in Servos.json - one row per position name.
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

        /// <summary>X, Y, Z or R: the jog button that drives it and its axis (E_Servo) in the PLC's recipe.</summary>
        public string Type { get; set; }

        /// <summary>Order the axis moves in when a position is moved to (1 first; 0 = after the numbered ones).</summary>
        public int MovementPriority { get; set; }

        /// <summary>The same for a position whose name contains "safe".</summary>
        public int SafePositionPriority { get; set; }
    }
}
