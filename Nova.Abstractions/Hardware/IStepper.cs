using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    /// <summary>
    /// A stepper motor as the Manual screen drives it. Two physical kinds exist
    /// (mirroring V3's StepperWrapper): axis-based steppers living on the motion
    /// card, and IO-based conveyor steppers driven purely by digital output bits.
    /// Both present the same contract; <see cref="ReadStatus"/> reuses the axis
    /// snapshot so the shared monitor can publish steppers and servos alike.
    /// </summary>
    public interface IStepper
    {
        string ComponentId { get; }

        /// <summary>True = motion-card axis; false = IO-driven conveyor.</summary>
        bool IsAxisBased { get; }

        Task<bool> EnableAsync();

        /// <summary>
        /// Highest speed <see cref="ForwardAsync"/> / <see cref="ReverseAsync"/> take, in the
        /// stepper's own unit (an IO stepper: % of its drive's speed range, so 100).
        /// </summary>
        double MaxSpeed { get; }

        /// <summary>
        /// The speed the stepper runs at in manual now, read from the device; null when it
        /// cannot be read (device not connected).
        /// </summary>
        Task<double?> ReadSpeedAsync();

        /// <summary>
        /// Runs until Stop. A speed &gt; 0 is set first (false if it is not taken, and the
        /// stepper is not started); speed &lt;= 0 keeps the speed the device has.
        /// </summary>
        Task<bool> ForwardAsync(double speed);
        Task<bool> ReverseAsync(double speed);
        Task<bool> StopAsync();
        Task ResetAlarmAsync();

        /// <summary>Called by the monitor on a background thread; must be quick and never throw.</summary>
        AxisStatusSnapshot ReadStatus();
    }
}
