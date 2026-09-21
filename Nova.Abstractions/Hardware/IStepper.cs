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

        /// <summary>Runs until Stop. speed &lt;= 0 uses the configured default (IO steppers ignore it).</summary>
        Task<bool> ForwardAsync(double speed);
        Task<bool> ReverseAsync(double speed);
        Task<bool> StopAsync();
        Task ResetAlarmAsync();

        /// <summary>Called by the monitor on a background thread; must be quick and never throw.</summary>
        AxisStatusSnapshot ReadStatus();
    }
}
