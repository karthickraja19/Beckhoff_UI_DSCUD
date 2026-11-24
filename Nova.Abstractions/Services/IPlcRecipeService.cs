using System.Threading.Tasks;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Writes the HMI's recipes into the PLC. The PLC drives every taught move from its own
    /// copy of the gantry recipe (one per gantry), and each robot from the speed in its
    /// command; the saved recipe files stay the HMI's, and <see cref="IRecipeService"/>
    /// still reads and saves them.
    /// </summary>
    public interface IPlcRecipeService
    {
        /// <summary>False when the PLC's recipe is not configured; <see cref="UnavailableReason"/> says why.</summary>
        bool IsAvailable { get; }

        /// <summary>Why <see cref="IsAvailable"/> is false; empty while available.</summary>
        string UnavailableReason { get; }

        /// <summary>
        /// Writes the module's saved gantry recipe into its gantry in the PLC. The task faults,
        /// with the reason as its message, when the module has no PLC gantry, the recipe does
        /// not fit the PLC's table, or the PLC cannot be written.
        /// </summary>
        Task WriteGantryRecipeAsync(string moduleName);

        /// <summary>
        /// Writes every saved robot recipe row mapped to a PLC member (Speed, ...) into every
        /// configured robot in the PLC. The task faults, with the reason as its message, when
        /// the PLC's robots are not configured, no robot is, no row is mapped, a value is out
        /// of its range or its member's type, a member is missing, or the PLC cannot be written.
        /// </summary>
        Task WriteRobotRecipeAsync();

        /// <summary>
        /// Writes the CNC channel's saved positions into its CNC recipe in the PLC. The task
        /// faults, with the reason as its message, when the PLC's CNC recipe is not
        /// configured, the channel has no PlcChannel, or the PLC cannot be written.
        /// </summary>
        Task WriteCncRecipeAsync(string channelName);
    }
}
