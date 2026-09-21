using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using Microsoft.Win32;
using Nova.Abstractions.Services;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// <see cref="ICsvExportService"/> using the WPF save dialog and V3's exact CSV
    /// shape (Report_Screen.writeCSV): header line of column names, then one line per
    /// row, comma separated, with any value containing a comma wrapped in quotes and
    /// DBNull written as empty.
    /// </summary>
    public sealed class CsvExportService : ICsvExportService
    {
        private readonly IDialogService _dialogs;

        public CsvExportService(IDialogService dialogs)
        {
            _dialogs = dialogs;
        }

        public bool Export(DataTable table, string suggestedFileName)
        {
            if (table == null || table.Rows.Count == 0)
            {
                _dialogs?.ShowInfo("No Data Available", "Export");
                return false;
            }

            var headers = new List<string>();
            foreach (DataColumn column in table.Columns)
                headers.Add(column.ColumnName);

            var rows = new List<IReadOnlyList<string>>();
            foreach (DataRow row in table.Rows)
            {
                var cells = new List<string>();
                for (int i = 0; i < table.Columns.Count; i++)
                    cells.Add(Convert.IsDBNull(row[i]) ? string.Empty : row[i].ToString());
                rows.Add(cells);
            }

            return Export(headers, rows, suggestedFileName);
        }

        public bool Export(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows,
            string suggestedFileName)
        {
            if (headers == null || headers.Count == 0 || rows == null || rows.Count == 0)
            {
                _dialogs?.ShowInfo("No Data Available", "Export");
                return false;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "csv files (*.csv)|*.csv",
                FileName = suggestedFileName,
                DefaultExt = ".csv",
                RestoreDirectory = true,
            };

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
                return false;

            try
            {
                var text = new StringBuilder();
                text.AppendLine(string.Join(",", Quote(headers)));
                foreach (var row in rows)
                    text.AppendLine(string.Join(",", Quote(row)));

                File.WriteAllText(dialog.FileName, text.ToString());
                return true;
            }
            catch (Exception ex)
            {
                _dialogs?.ShowError("Failed to save the CSV: " + ex.Message, "Export");
                return false;
            }
        }

        /// <summary>V3 quotes only values containing a comma; matched here.</summary>
        private static IEnumerable<string> Quote(IReadOnlyList<string> values)
        {
            foreach (var value in values)
            {
                string cell = value ?? string.Empty;
                yield return cell.IndexOf(',') >= 0 ? "\"" + cell + "\"" : cell;
            }
        }
    }
}
