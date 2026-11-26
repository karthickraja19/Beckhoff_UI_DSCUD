using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Nova.Abstractions.Data.Models;

namespace Nova.Core.Services
{
    /// <summary>A gantry's recipe as its file holds it: the axes (column groups, in order) and the positions (rows, in order).</summary>
    internal sealed class GantryRecipeTable
    {
        public string Path { get; set; }
        public List<string> Axes { get; set; } = new List<string>();
        public List<GantryPosition> Positions { get; set; } = new List<GantryPosition>();
    }

    /// <summary>
    /// The recipe file of one gantry: <c>{data folder}\{gantry Name}.xlsx</c>, or
    /// <c>{gantry Name}.csv</c> when there is no .xlsx. Both are laid out the same way:
    /// <code>
    ///   row 1:  Positions | {servo 1}                                  | {servo 2}   ...
    ///   row 2:            | Value | Speed | Acceleration | Deceleration | Value | ...
    ///   row 3+: Safe_Pos  | 0     | 0     | 0            | 0            | ...
    /// </code>
    /// One group of four columns per servo (its EnumName in Servos.json), from column B; the
    /// groups end at the first empty name in row 1. The positions end at the first row whose
    /// column A is empty. A blank number is 0. The order of the groups is the axis number in
    /// the PLC (E_Servo of that gantry), the order of the rows the position order.
    ///
    /// The .xlsx is read and written as the Office Open XML it is (no Excel, no library):
    /// on Save only the number cells of the position rows (and a renamed position) are
    /// replaced in the first worksheet, so the header, the merged cells, widths and styles
    /// stay as they were. A cell holding a formula is refused rather than overwritten.
    /// The file is written to a temporary file next to it and then swapped in, so a Save that
    /// fails - the file is open in Excel, say - leaves the old file whole.
    /// </summary>
    internal static class GantryRecipeFile
    {
        private const int FirstAxisColumn = 1;      // B
        private const int ColumnsPerAxis = 4;       // Value, Speed, Acceleration, Deceleration
        private const int FirstPositionRow = 2;     // row 3

        private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace OfficeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        /// <summary>The gantry's recipe file in the data folder - .xlsx first, then .csv - or null when it has neither.</summary>
        public static string Find(string dataDirectory, string gantryName)
        {
            string name = (gantryName ?? string.Empty).Trim();
            if (name.Length == 0)
                return null;

            foreach (string extension in new[] { ".xlsx", ".csv" })
            {
                string path = System.IO.Path.Combine(dataDirectory, name + extension);
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        /// <summary>The file names looked for, for a message when there is none.</summary>
        public static string Expected(string dataDirectory, string gantryName)
        {
            string name = (gantryName ?? string.Empty).Trim();
            return System.IO.Path.Combine(dataDirectory, name + ".xlsx") + " (or " + name + ".csv)";
        }

        /// <summary>Reads the file. Throws <see cref="InvalidDataException"/> naming the cell for a layout or number it cannot use.</summary>
        public static GantryRecipeTable Read(string path)
        {
            Dictionary<long, string> cells = IsXlsx(path) ? ReadXlsxCells(path) : ReadCsvCells(path);
            return Parse(path, cells);
        }

        /// <summary>
        /// Writes the positions into the file. The axes must be the file's own, in its order;
        /// a position is a row, in the order given. Throws <see cref="IOException"/> when the
        /// file cannot be replaced (open in another program).
        /// </summary>
        public static void Write(string path, IReadOnlyList<string> axes, IReadOnlyList<GantryPosition> positions)
        {
            string temp = path + ".saving";
            try
            {
                if (IsXlsx(path))
                {
                    File.WriteAllBytes(temp, XlsxWithPositions(path, axes, positions));
                }
                else
                {
                    CheckSameAxes(System.IO.Path.GetFileName(path), Parse(path, ReadCsvCells(path)).Axes, axes);
                    File.WriteAllText(temp, CsvText(axes, positions), new UTF8Encoding(true));
                }

                File.Replace(temp, path, null);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        private static bool IsXlsx(string path) => string.Equals(System.IO.Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase);

        // ==================================================================
        // The layout, the same for both formats
        // ==================================================================

        private static long Key(int row, int column) => ((long)row << 20) | (uint)column;

        private static string At(Dictionary<long, string> cells, int row, int column)
        {
            return cells.TryGetValue(Key(row, column), out string value) && value != null ? value.Trim() : string.Empty;
        }

        /// <summary>The A1-style name of a 0-based cell, for messages and the sheet XML.</summary>
        public static string CellName(int row, int column)
        {
            var letters = new StringBuilder();
            int n = column + 1;
            while (n > 0)
            {
                int rem = (n - 1) % 26;
                letters.Insert(0, (char)('A' + rem));
                n = (n - 1) / 26;
            }

            return letters.ToString() + (row + 1).ToString(CultureInfo.InvariantCulture);
        }

        private static GantryRecipeTable Parse(string path, Dictionary<long, string> cells)
        {
            string file = System.IO.Path.GetFileName(path);
            var table = new GantryRecipeTable { Path = path };

            for (int column = FirstAxisColumn; ; column += ColumnsPerAxis)
            {
                string axis = At(cells, 0, column);
                if (axis.Length == 0)
                    break;
                if (table.Axes.Any(a => string.Equals(a, axis, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException(file + ": the servo " + axis + " heads two column groups (" + CellName(0, column) + ").");

                table.Axes.Add(axis);
            }

            if (table.Axes.Count == 0)
            {
                throw new InvalidDataException(file + ": no servo name in row 1 from B1 on. Row 1 names each servo over its four columns "
                    + "(Value, Speed, Acceleration, Deceleration), row 2 names those columns, the positions start in row 3.");
            }

            // The four columns of a group by their row-2 heading; a group without recognisable
            // headings is taken in the order Value, Speed, Acceleration, Deceleration.
            var memberColumns = new List<int[]>();
            for (int a = 0; a < table.Axes.Count; a++)
            {
                int first = FirstAxisColumn + a * ColumnsPerAxis;
                int[] order = { first, first + 1, first + 2, first + 3 };
                int[] byHeading = new int[ColumnsPerAxis];
                bool recognised = true;
                var seen = new HashSet<int>();
                for (int i = 0; i < ColumnsPerAxis; i++)
                {
                    int member = MemberOf(At(cells, 1, first + i));
                    if (member < 0 || !seen.Add(member))
                    {
                        recognised = false;
                        break;
                    }

                    byHeading[member] = first + i;
                }

                memberColumns.Add(recognised ? byHeading : order);
            }

            for (int row = FirstPositionRow; ; row++)
            {
                string name = At(cells, row, 0);
                if (name.Length == 0)
                    break;
                if (table.Positions.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException(file + ": the position " + name + " is in two rows (" + CellName(row, 0) + ").");

                var position = new GantryPosition { Name = name };
                for (int a = 0; a < table.Axes.Count; a++)
                {
                    int[] columns = memberColumns[a];
                    position.Axes[table.Axes[a]] = new GantryAxisMove
                    {
                        Value = Number(cells, file, row, columns[0]),
                        Speed = Number(cells, file, row, columns[1]),
                        Acceleration = Number(cells, file, row, columns[2]),
                        Deceleration = Number(cells, file, row, columns[3]),
                    };
                }

                table.Positions.Add(position);
            }

            return table;
        }

        /// <summary>0 Value, 1 Speed, 2 Acceleration, 3 Deceleration from a row-2 heading; -1 for anything else.</summary>
        private static int MemberOf(string heading)
        {
            string h = heading.ToLowerInvariant();
            if (h.StartsWith("val") || h.StartsWith("pos")) return 0;
            if (h.StartsWith("speed") || h.StartsWith("vel")) return 1;
            if (h.StartsWith("acc")) return 2;
            if (h.StartsWith("dec") || h.StartsWith("deacc")) return 3;
            return -1;
        }

        private static double Number(Dictionary<long, string> cells, string file, int row, int column)
        {
            string raw = At(cells, row, column);
            if (raw.Length == 0)
                return 0d;

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return value;
            }

            throw new InvalidDataException(file + " " + CellName(row, column) + " is '" + raw + "', not a number.");
        }

        private static string Text(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        // ==================================================================
        // .xlsx
        // ==================================================================

        /// <summary>Opened for reading while Excel may hold it open.</summary>
        private static byte[] ReadAllShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                return copy.ToArray();
            }
        }

        private static XDocument LoadEntry(ZipArchiveEntry entry)
        {
            using (Stream stream = entry.Open())
            {
                return XDocument.Load(stream);
            }
        }

        /// <summary>The first worksheet's part name, e.g. xl/worksheets/sheet1.xml.</summary>
        private static string FirstSheetPart(ZipArchive zip, string file)
        {
            ZipArchiveEntry workbookEntry = zip.GetEntry("xl/workbook.xml");
            ZipArchiveEntry relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbookEntry == null || relsEntry == null)
                throw new InvalidDataException(file + " is not an Excel workbook (.xlsx).");

            XElement sheet = LoadEntry(workbookEntry).Root?.Element(Main + "sheets")?.Elements(Main + "sheet").FirstOrDefault();
            string id = (string)sheet?.Attribute(OfficeRel + "id");
            if (id == null)
                throw new InvalidDataException(file + " has no worksheet.");

            string target = LoadEntry(relsEntry).Root?.Elements(PackageRel + "Relationship")
                .Where(r => (string)r.Attribute("Id") == id)
                .Select(r => (string)r.Attribute("Target"))
                .FirstOrDefault();
            if (string.IsNullOrEmpty(target))
                throw new InvalidDataException(file + ": its first worksheet cannot be found.");

            target = target.Replace('\\', '/');
            return target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
        }

        private static List<string> SharedStrings(ZipArchive zip)
        {
            var strings = new List<string>();
            ZipArchiveEntry entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
                return strings;

            foreach (XElement si in LoadEntry(entry).Root.Elements(Main + "si"))
                strings.Add(string.Concat(si.Descendants(Main + "t").Select(t => t.Value)));

            return strings;
        }

        /// <summary>0-based (row, column) from an A1 reference; false for one it cannot read.</summary>
        private static bool ParseReference(string reference, out int row, out int column)
        {
            row = -1;
            column = -1;
            if (string.IsNullOrEmpty(reference))
                return false;

            int i = 0;
            int col = 0;
            while (i < reference.Length && char.IsLetter(reference[i]))
            {
                col = col * 26 + (char.ToUpperInvariant(reference[i]) - 'A' + 1);
                i++;
            }

            if (col == 0 || !int.TryParse(reference.Substring(i), NumberStyles.None, CultureInfo.InvariantCulture, out int r) || r < 1)
                return false;

            row = r - 1;
            column = col - 1;
            return true;
        }

        private static Dictionary<long, string> ReadXlsxCells(string path)
        {
            string file = System.IO.Path.GetFileName(path);
            var cells = new Dictionary<long, string>();
            try
            {
                using (var zip = new ZipArchive(new MemoryStream(ReadAllShared(path)), ZipArchiveMode.Read))
                {
                    List<string> shared = SharedStrings(zip);
                    ZipArchiveEntry sheetEntry = zip.GetEntry(FirstSheetPart(zip, file));
                    if (sheetEntry == null)
                        throw new InvalidDataException(file + ": its first worksheet cannot be found.");

                    XElement sheetData = LoadEntry(sheetEntry).Root?.Element(Main + "sheetData");
                    foreach (XElement rowElement in sheetData?.Elements(Main + "row") ?? Enumerable.Empty<XElement>())
                    {
                        int column = -1;
                        foreach (XElement c in rowElement.Elements(Main + "c"))
                        {
                            int row;
                            if (!ParseReference((string)c.Attribute("r"), out row, out column))
                            {
                                // A cell without a reference follows the one before it.
                                row = (int?)rowElement.Attribute("r") - 1 ?? 0;
                                column++;
                            }

                            string type = (string)c.Attribute("t");
                            string value;
                            if (type == "s")
                            {
                                int index;
                                value = int.TryParse(c.Element(Main + "v")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
                                    && index >= 0 && index < shared.Count ? shared[index] : string.Empty;
                            }
                            else if (type == "inlineStr")
                            {
                                value = string.Concat(c.Descendants(Main + "t").Select(t => t.Value));
                            }
                            else
                            {
                                value = c.Element(Main + "v")?.Value ?? string.Empty;
                            }

                            cells[Key(row, column)] = value;
                        }
                    }
                }
            }
            catch (InvalidDataException ex) when (!ex.Message.StartsWith(file, StringComparison.Ordinal))
            {
                throw new InvalidDataException(file + " is not a readable Excel workbook (.xlsx): " + ex.Message, ex);
            }

            return cells;
        }

        /// <summary>The workbook with the position rows of its first worksheet replaced.</summary>
        private static byte[] XlsxWithPositions(string path, IReadOnlyList<string> axes, IReadOnlyList<GantryPosition> positions)
        {
            string file = System.IO.Path.GetFileName(path);
            byte[] original = ReadAllShared(path);
            GantryRecipeTable current = Parse(path, ReadXlsxCells(path));
            CheckSameAxes(file, current.Axes, axes);

            var buffer = new MemoryStream();
            buffer.Write(original, 0, original.Length);
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, true))
            {
                string part = FirstSheetPart(zip, file);
                ZipArchiveEntry sheetEntry = zip.GetEntry(part);
                XDocument sheet = LoadEntry(sheetEntry);
                XElement sheetData = sheet.Root.Element(Main + "sheetData");

                int lastColumn = FirstAxisColumn + axes.Count * ColumnsPerAxis - 1;
                for (int p = 0; p < positions.Count; p++)
                {
                    int row = FirstPositionRow + p;
                    GantryPosition position = positions[p];
                    string oldName = p < current.Positions.Count ? current.Positions[p].Name : null;
                    if (!string.Equals(oldName, position.Name, StringComparison.Ordinal))
                        SetText(sheetData, row, 0, position.Name, file);

                    for (int a = 0; a < axes.Count; a++)
                    {
                        position.Axes.TryGetValue(axes[a], out GantryAxisMove move);
                        move = move ?? new GantryAxisMove();
                        int first = FirstAxisColumn + a * ColumnsPerAxis;
                        SetNumber(sheetData, row, first, move.Value, file);
                        SetNumber(sheetData, row, first + 1, move.Speed, file);
                        SetNumber(sheetData, row, first + 2, move.Acceleration, file);
                        SetNumber(sheetData, row, first + 3, move.Deceleration, file);
                    }
                }

                // Rows of positions that are no longer there.
                for (int p = positions.Count; p < current.Positions.Count; p++)
                    RowElement(sheetData, FirstPositionRow + p, false)?.Remove();

                XElement dimension = sheet.Root.Element(Main + "dimension");
                if (dimension != null)
                {
                    int lastRow = Math.Max(1, FirstPositionRow + positions.Count - 1);
                    dimension.SetAttributeValue("ref", "A1:" + CellName(lastRow, lastColumn));
                }

                sheetEntry.Delete();
                ZipArchiveEntry replaced = zip.CreateEntry(part, CompressionLevel.Optimal);
                using (Stream stream = replaced.Open())
                {
                    sheet.Save(stream, SaveOptions.DisableFormatting);
                }
            }

            return buffer.ToArray();
        }

        private static void CheckSameAxes(string file, IReadOnlyList<string> inFile, IReadOnlyList<string> toWrite)
        {
            if (inFile.Count != toWrite.Count
                || inFile.Where((axis, i) => !string.Equals(axis, toWrite[i], StringComparison.OrdinalIgnoreCase)).Any())
            {
                throw new InvalidDataException(file + " now has the servos " + string.Join(", ", inFile) + ", not "
                    + string.Join(", ", toWrite) + " as the page showed: it was changed meanwhile. Open the page again. Nothing was saved.");
            }
        }

        /// <summary>The row element of a 0-based row; created in order when <paramref name="create"/>.</summary>
        private static XElement RowElement(XElement sheetData, int row, bool create)
        {
            int wanted = row + 1;
            XElement after = null;
            foreach (XElement element in sheetData.Elements(Main + "row"))
            {
                int r = (int?)element.Attribute("r") ?? 0;
                if (r == wanted)
                    return element;
                if (r < wanted)
                    after = element;
                else
                    break;
            }

            if (!create)
                return null;

            var created = new XElement(Main + "row", new XAttribute("r", wanted));
            if (after != null)
                after.AddAfterSelf(created);
            else
                sheetData.AddFirst(created);
            return created;
        }

        /// <summary>The cell element, created in column order (with the style of its row's neighbour left to Excel's default).</summary>
        private static XElement CellElement(XElement sheetData, int row, int column)
        {
            XElement rowElement = RowElement(sheetData, row, true);
            string reference = CellName(row, column);
            XElement after = null;
            foreach (XElement c in rowElement.Elements(Main + "c"))
            {
                if (!ParseReference((string)c.Attribute("r"), out _, out int col))
                    continue;
                if (col == column)
                    return c;
                if (col < column)
                    after = c;
                else
                    break;
            }

            var created = new XElement(Main + "c", new XAttribute("r", reference));
            if (after != null)
                after.AddAfterSelf(created);
            else
                rowElement.AddFirst(created);

            // A row's "spans" is only a hint; a new cell may fall outside it.
            rowElement.Attribute("spans")?.Remove();
            return created;
        }

        private static void RefuseFormula(XElement cell, string file)
        {
            if (cell.Element(Main + "f") != null)
            {
                throw new InvalidDataException(file + " " + (string)cell.Attribute("r")
                    + " holds a formula; the recipe writes numbers there. Type the value in Excel instead. Nothing was saved.");
            }
        }

        private static void SetNumber(XElement sheetData, int row, int column, double value, string file)
        {
            XElement cell = CellElement(sheetData, row, column);
            RefuseFormula(cell, file);
            cell.Attribute("t")?.Remove();
            cell.Elements().Remove();
            cell.Add(new XElement(Main + "v", Text(value)));
        }

        private static void SetText(XElement sheetData, int row, int column, string text, string file)
        {
            XElement cell = CellElement(sheetData, row, column);
            RefuseFormula(cell, file);
            cell.SetAttributeValue("t", "inlineStr");
            cell.Elements().Remove();
            cell.Add(new XElement(Main + "is", new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text ?? string.Empty)));
        }

        // ==================================================================
        // .csv
        // ==================================================================

        private static Dictionary<long, string> ReadCsvCells(string path)
        {
            string text = Encoding.UTF8.GetString(ReadAllShared(path));
            if (text.Length > 0 && text[0] == '﻿')
                text = text.Substring(1);

            string firstLine = text.Split('\n')[0];
            char separator = firstLine.Count(ch => ch == ';') > firstLine.Count(ch => ch == ',') ? ';' : ',';

            var cells = new Dictionary<long, string>();
            int row = 0;
            int column = 0;
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else if (ch == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        field.Append(ch);
                    }

                    continue;
                }

                if (ch == '"')
                {
                    quoted = true;
                }
                else if (ch == separator)
                {
                    cells[Key(row, column)] = field.ToString();
                    field.Clear();
                    column++;
                }
                else if (ch == '\n')
                {
                    cells[Key(row, column)] = field.ToString();
                    field.Clear();
                    row++;
                    column = 0;
                }
                else if (ch != '\r')
                {
                    field.Append(ch);
                }
            }

            if (field.Length > 0 || column > 0)
                cells[Key(row, column)] = field.ToString();

            return cells;
        }

        private static string CsvField(string value)
        {
            value = value ?? string.Empty;
            return value.IndexOfAny(new[] { ',', ';', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        private static string CsvText(IReadOnlyList<string> axes, IReadOnlyList<GantryPosition> positions)
        {
            var text = new StringBuilder();
            var header = new List<string> { "Positions" };
            var members = new List<string> { string.Empty };
            foreach (string axis in axes)
            {
                header.AddRange(new[] { axis, string.Empty, string.Empty, string.Empty });
                members.AddRange(new[] { "Value", "Speed", "Acceleration", "Deceleration" });
            }

            text.AppendLine(string.Join(",", header.Select(CsvField)));
            text.AppendLine(string.Join(",", members.Select(CsvField)));
            foreach (GantryPosition position in positions)
            {
                var line = new List<string> { CsvField(position.Name) };
                foreach (string axis in axes)
                {
                    position.Axes.TryGetValue(axis, out GantryAxisMove move);
                    move = move ?? new GantryAxisMove();
                    line.AddRange(new[] { Text(move.Value), Text(move.Speed), Text(move.Acceleration), Text(move.Deceleration) });
                }

                text.AppendLine(string.Join(",", line));
            }

            return text.ToString();
        }
    }
}
