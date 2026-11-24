using System;
using System.Threading.Tasks;

namespace Nova.Abstractions.Data.Repositories
{
    /// <summary>
    /// Write side of the production record: one row per finished unit, in the table the
    /// Home and Data pages read through <see cref="IProductionRepository"/>.
    /// </summary>
    public interface IProductionRecorder
    {
        /// <summary>
        /// Saves one finished unit. Date and Shift are filled in from the shift the unit
        /// started in when the record leaves them empty. Faults when it cannot be saved.
        /// </summary>
        Task RecordAsync(ProductionUnitRecord unit);
    }
}
