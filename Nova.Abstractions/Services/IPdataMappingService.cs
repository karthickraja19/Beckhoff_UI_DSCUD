using System.Collections.Generic;
using Nova.Abstractions.Data.Models;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Backing store for the PDATA Mapping tab of the Setting page: the field catalogue, the
    /// saved machine profiles under PDATA\, and the query template written beside them.
    /// </summary>
    public interface IPdataMappingService
    {
        /// <summary>
        /// Every mappable PDATA field name, in the order the operator sees in the picker
        /// (Data\Settings\PdataFields.json).
        /// </summary>
        IReadOnlyList<string> GetAvailableFields();

        /// <summary>Saved machine profile names, or the built-in default when none exist yet.</summary>
        IReadOnlyList<string> GetMachineNames();

        /// <summary>Loads a saved profile, or null when there is no file for it.</summary>
        PdataMachineConfig LoadMachineConfig(string machineName);

        /// <summary>
        /// Writes a profile to PDATA\{machine}.json. Returns null when the write failed -
        /// the implementation has already reported it, and the caller should stay in edit
        /// mode rather than treat the attempt as a save.
        /// </summary>
        RecipeSaveResult SaveMachineConfig(PdataMachineConfig config);

        /// <summary>Writes the query template to PDATA\{machine}_template.json.</summary>
        void SaveQueryTemplate(PdataQueryTemplate template);

        /// <summary>
        /// Prompts for a CSV and returns the field names found in it, or null when the
        /// operator cancels. Names are returned as written; matching is the caller's job.
        /// </summary>
        IReadOnlyList<string> ImportFieldNamesFromCsv();
    }
}
