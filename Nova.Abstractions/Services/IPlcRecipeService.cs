using System.Threading.Tasks;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Writes the HMI's gantry recipes into the PLC. The PLC drives every taught move from
    /// its own copy of the recipe (one per gantry); the saved recipe files stay the HMI's,
    /// and <see cref="IRecipeService"/> still reads and saves them.
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
    }
}
