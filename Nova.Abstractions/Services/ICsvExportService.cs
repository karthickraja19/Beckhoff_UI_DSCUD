using System.Collections.Generic;
using System.Data;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Saves a loaded report to CSV, the only export V3 has — every report screen
    /// (Report_Screen's three tabs and AlarmScreen) offers SaveFileDialog -&gt; writeCSV
    /// and nothing else. Owns the file dialog so view models stay frame-agnostic;
    /// returns false when the operator cancels.
    /// </summary>
    public interface ICsvExportService
    {
        /// <summary>Saves a stored-procedure result, columns exactly as returned.</summary>
        bool Export(DataTable table, string suggestedFileName);

        /// <summary>
        /// Saves projected rows. <paramref name="headers"/> is the column line;
        /// each row must have the same number of cells.
        /// </summary>
        bool Export(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows,
            string suggestedFileName);
    }
}
