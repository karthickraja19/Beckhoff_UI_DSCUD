using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Live values of the machine's critical parameters, by key name, for the Home
    /// page's Parameter Dashboard (Audio SW DFM v5.4 slide 43: "Follow AME definition
    /// on critical params").
    /// </summary>
    /// <remarks>
    /// The keys are the parameter names of the production spec limits (Config &gt; Spec
    /// Limits), so the dashboard and the Config page always list the same parameters
    /// against the same limits.
    /// </remarks>
    public interface IParameterValueSource
    {
        /// <summary>The current value of each key it can read. A key it cannot read is left out.</summary>
        Task<IReadOnlyDictionary<string, double>> ReadAsync(IReadOnlyCollection<string> keys);
    }
}
