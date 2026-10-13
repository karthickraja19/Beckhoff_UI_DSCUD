using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    /// <summary>
    /// Represents a single physical or virtual motion axis.
    /// Speeds are passed per call (they come from operator input on the Manual
    /// screen); acceleration/deceleration and timeouts are owned by the
    /// implementation, which reads them from its axis parameter configuration.
    /// Motion methods return true when the motion completed successfully.
    /// </summary>
    public interface IAxis
    {
        string ComponentId { get; }
        bool IsHomed { get; }

        /// <summary>
        /// False for an axis that can only jog - an axis stepper: the Axis tab then disables
        /// Move, Home and point-to-point for it, and <see cref="MoveAbsAsync"/>,
        /// <see cref="MoveRelAsync"/> and <see cref="HomeAsync"/> return false.
        /// </summary>
        bool SupportsPositioning { get; }

        Task<bool> EnableAsync();
        Task<bool> DisableAsync();
        Task ResetAsync();
        Task StopAsync();

        Task<bool> MoveAbsAsync(double position, double speed);
        Task<bool> MoveRelAsync(double distance, double speed);
        Task<bool> HomeAsync(double velocity);

        /// <summary>Starts a continuous jog; runs until <see cref="JogStopAsync"/> or <see cref="StopAsync"/>.</summary>
        Task JogStartAsync(bool forward, double speed);
        Task JogStopAsync();

        /// <summary>
        /// The current live status. Called by the axis monitor on a background thread, and
        /// from the UI thread when a screen needs the status straight away (the Axis tab on
        /// an axis change); implementations should return quickly and never throw.
        /// </summary>
        AxisStatusSnapshot ReadStatus();
    }
}
