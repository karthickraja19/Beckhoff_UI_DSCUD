using System.Threading;
using System.Threading.Tasks;

namespace Master.Sequences.FiveAxis
{
    /// <summary>
    /// Coordinated moves of the five axes. The CNC channel's axes cannot be positioned one by one
    /// (the HMI only jogs them), so a pose is sent to the channel as a G-code block and the five axes
    /// move together. Implemented in Nova.Wpf.App over the PLC's CNC interface (ADS).
    /// </summary>
    public interface IFiveAxisMotion
    {
        /// <summary>False when the PLC interface for it is not mapped; <see cref="UnavailableReason"/> says what is missing.</summary>
        bool IsAvailable { get; }

        string UnavailableReason { get; }

        /// <summary>Where the five axes are now (mm and degrees).</summary>
        FiveAxisPose ReadPose(int station, FiveAxisSettings settings);

        /// <summary>
        /// Moves X, Y, Z, A and C to <paramref name="target"/> as one G-code move and returns when the channel
        /// has finished and every axis is in position. Throws when it does not get there.
        /// </summary>
        Task MoveAsync(int station, FiveAxisPose target, FiveAxisSettings settings, CancellationToken token);

        /// <summary>
        /// Stops the channel's program (cycle stop). <paramref name="reset"/> also resets the channel, which the Stop
        /// button does but a fault does not: the operator needs to see the channel's error. Never throws.
        /// </summary>
        Task StopAsync(int station, bool reset);
    }
}
