using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nova.Abstractions.Services
{
    /// <summary>A sensor's state on the Sensor Connection Dashboard (Audio SW DFM v5.4 slide 55).</summary>
    public enum SensorState
    {
        /// <summary>Sensor is idle - nothing in front of it.</summary>
        Idle,
        /// <summary>Sensor is activated.</summary>
        Activated,
        /// <summary>Sensor is manually disabled.</summary>
        Disabled,
        /// <summary>Sensor signal lost.</summary>
        SignalLost
    }

    public sealed class SensorStatus
    {
        /// <summary>The sensor's tag on the machine layout, e.g. "A-3-2".</summary>
        public string Id { get; set; }

        public string Name { get; set; }

        /// <summary>The location or module it watches, e.g. "Transfer conveyor".</summary>
        public string Location { get; set; }

        /// <summary>
        /// Slide 55: a critical sensor's disablement "may cause safety issue", e.g. one
        /// that finds the carrier. Only non-critical sensors may be disabled by hand.
        /// </summary>
        public bool IsCritical { get; set; }

        public SensorState State { get; set; }
    }

    /// <summary>Whether a unit is at one location on the machine (slide 55: "Unit at location A").</summary>
    public sealed class UnitLocationStatus
    {
        public string Name { get; set; }

        public bool HasUnit { get; set; }
    }

    /// <summary>
    /// Live sensor states and unit positions for the Sensor Connection Dashboard, and
    /// the request to disable a non-critical sensor. The PLC owns the interlocks, so
    /// disabling is a request it may refuse.
    /// </summary>
    public interface ISensorService
    {
        IReadOnlyList<SensorStatus> ReadSensors();

        IReadOnlyList<UnitLocationStatus> ReadLocations();

        /// <summary>
        /// Asks for a sensor to be disabled or re-enabled. Returns false when refused -
        /// a critical sensor, or one the machine does not know.
        /// </summary>
        Task<bool> SetDisabledAsync(string sensorId, bool disabled);

        /// <summary>Raised when any state changes. May be on a background thread.</summary>
        event EventHandler Changed;
    }
}
