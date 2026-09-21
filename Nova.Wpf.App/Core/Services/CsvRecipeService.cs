using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    public sealed class CsvRecipeService : IRecipeService
    {
        private const string GantryStructureFile = "GantryStructure.xml";
        private const string AxisParametersFile = "Axis_Parameters.xml";
        private const string SettingsFolder = "Settings";
        private const string RobotRecipeFile = "RoboReceipe_Param.csv";
        private const string LightRecipeFile = "LightControl_Param.csv";
        private const string EditLogFile = "Recipe_Edit_Log.csv";

        private readonly string _dataDirectory;
        private readonly IAppSession _appSession;

        public event EventHandler RecipeChanged;

        public CsvRecipeService(string dataDirectory, IAppSession appSession)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                throw new ArgumentException("Data directory cannot be empty.", nameof(dataDirectory));
            }

            _dataDirectory = dataDirectory;
            _appSession = appSession ?? throw new ArgumentNullException(nameof(appSession));
        }

        public IReadOnlyList<string> GetGantryModules()
        {
            var root = LoadGantryStructureRoot();
            return root.Elements()
                .Select(e => e.Name.LocalName.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name) && !name.Equals("Select", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public GantryModuleDefinition GetGantryModuleDefinition(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                throw new ArgumentException("Module name is required.", nameof(moduleName));
            }

            var root = LoadGantryStructureRoot();
            var moduleElement = root.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
            if (moduleElement == null)
            {
                throw new InvalidDataException("Module '" + moduleName + "' not found in " + GantryStructureFile + ".");
            }

            return new GantryModuleDefinition
            {
                ModuleName = moduleElement.Name.LocalName.Trim(),
                MovementPriority = ReadPriority(moduleElement, "MovementPriority"),
                SafePositionPriority = ReadPriority(moduleElement, "SafePosition_Priority"),
            };
        }

        public IReadOnlyDictionary<string, double> GetAxisSpeedLimits()
        {
            string axisPath = Path.Combine(_dataDirectory, SettingsFolder, AxisParametersFile);
            if (!File.Exists(axisPath))
            {
                throw new FileNotFoundException("Axis parameter file not found.", axisPath);
            }

            var root = XDocument.Load(axisPath).Root;
            if (root == null)
            {
                throw new InvalidDataException("Axis parameter XML has no root element.");
            }

            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var axisNode in root.Elements())
            {
                string axisName = ReadAxisName(axisNode);
                if (string.IsNullOrWhiteSpace(axisName))
                {
                    continue;
                }

                if (!TryReadHomingSpeed(axisNode, out double speed))
                {
                    continue;
                }

                result[axisName] = speed;
            }

            return result;
        }

        public DynamicTableDocument LoadGantryRecipeDynamic(string moduleName)
        {
            var records = LoadGantryRecipe(moduleName);
            var columns = new List<DynamicColumnSchema>
            {
                new DynamicColumnSchema { Key = "Position", Title = "Position", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = "X", Title = "X", IsReadOnly = false },
                new DynamicColumnSchema { Key = "XVel", Title = "X-Vel (mm)", IsReadOnly = false },
                new DynamicColumnSchema { Key = "XAcc", Title = "X-Axis Acc (mm/s)", IsReadOnly = false },
                new DynamicColumnSchema { Key = "XDeAcc", Title = "X-Axis De-Acc", IsReadOnly = false },
                new DynamicColumnSchema { Key = "Y", Title = "Y", IsReadOnly = false },
                new DynamicColumnSchema { Key = "YVel", Title = "Y-Vel (mm)", IsReadOnly = false },
                new DynamicColumnSchema { Key = "YAcc", Title = "Y-Axis Acc (mm/s)", IsReadOnly = false },
                new DynamicColumnSchema { Key = "YDeAcc", Title = "Y-Axis De-acc", IsReadOnly = false },
                new DynamicColumnSchema { Key = "Z", Title = "Z", IsReadOnly = false },
                new DynamicColumnSchema { Key = "ZVel", Title = "Z-Vel (mm)", IsReadOnly = false },
                new DynamicColumnSchema { Key = "ZAcc", Title = "Z-Axis Acc (mm/s)", IsReadOnly = false },
                new DynamicColumnSchema { Key = "ZDeAcc", Title = "Z-Axis De-acc", IsReadOnly = false },
            };
            var rows = records.Select(r => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Position"] = r.Position,
                ["X"] = r.X,
                ["XVel"] = r.XVel,
                ["XAcc"] = r.XAcc,
                ["XDeAcc"] = r.XDeAcc,
                ["Y"] = r.Y,
                ["YVel"] = r.YVel,
                ["YAcc"] = r.YAcc,
                ["YDeAcc"] = r.YDeAcc,
                ["Z"] = r.Z,
                ["ZVel"] = r.ZVel,
                ["ZAcc"] = r.ZAcc,
                ["ZDeAcc"] = r.ZDeAcc,
            }).ToList();
            return new DynamicTableDocument { Title = moduleName, Columns = columns, Rows = rows };
        }

        public RecipeSaveResult SaveGantryRecipeDynamic(string moduleName, DynamicTableDocument document)
        {
            if (document == null || document.Rows == null) throw new ArgumentNullException(nameof(document));
            var list = document.Rows.Select(r => new GantryPositionRecord
            {
                Position = r.TryGetValue("Position", out var pos) ? pos : string.Empty,
                X = r.TryGetValue("X", out var x) ? x : string.Empty,
                XVel = r.TryGetValue("XVel", out var xv) ? xv : string.Empty,
                XAcc = r.TryGetValue("XAcc", out var xa) ? xa : string.Empty,
                XDeAcc = r.TryGetValue("XDeAcc", out var xda) ? xda : string.Empty,
                Y = r.TryGetValue("Y", out var y) ? y : string.Empty,
                YVel = r.TryGetValue("YVel", out var yv) ? yv : string.Empty,
                YAcc = r.TryGetValue("YAcc", out var ya) ? ya : string.Empty,
                YDeAcc = r.TryGetValue("YDeAcc", out var yda) ? yda : string.Empty,
                Z = r.TryGetValue("Z", out var z) ? z : string.Empty,
                ZVel = r.TryGetValue("ZVel", out var zv) ? zv : string.Empty,
                ZAcc = r.TryGetValue("ZAcc", out var za) ? za : string.Empty,
                ZDeAcc = r.TryGetValue("ZDeAcc", out var zda) ? zda : string.Empty,
            }).ToList();
            return SaveGantryRecipe(moduleName, list);
        }

        public DynamicTableDocument LoadLightRecipeDynamic()
        {
            var records = LoadLightRecipe();
            var columns = new List<DynamicColumnSchema>
            {
                new DynamicColumnSchema { Key = "ColourCode", Title = "Colour Code", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = "ColourName", Title = "Colour", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = "Value", Title = "Value", IsReadOnly = false },
            };
            var rows = records.Select(r => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ColourCode"] = r.ColourCode,
                ["ColourName"] = r.Colour,
                ["Value"] = r.Value,
            }).ToList();
            return new DynamicTableDocument { Title = "Light Control", Columns = columns, Rows = rows };
        }

        public RecipeSaveResult SaveLightRecipeDynamic(DynamicTableDocument document)
        {
            if (document == null || document.Rows == null) throw new ArgumentNullException(nameof(document));
            var list = document.Rows.Select(r => new LightChannelRecord
            {
                ColourCode = r.TryGetValue("ColourCode", out var cc) ? cc : string.Empty,
                Colour = r.TryGetValue("ColourName", out var cn) ? cn : (r.TryGetValue("Colour", out var c) ? c : string.Empty),
                Value = r.TryGetValue("Value", out var v) ? v : string.Empty,
            }).ToList();
            return SaveLightRecipe(list);
        }

        public IReadOnlyList<GantryPositionRecord> LoadGantryRecipe(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                throw new ArgumentException("Module name is required.", nameof(moduleName));
            }

            string path = GetGantryRecipePath(moduleName);
            var rows = ReadCsvRows(path);
            var result = new List<GantryPositionRecord>(rows.Count);
            foreach (var row in rows)
            {
                result.Add(new GantryPositionRecord
                {
                    Position = GetCell(row, 0),
                    X = GetCell(row, 1),
                    XVel = GetCell(row, 2),
                    XAcc = GetCell(row, 3),
                    XDeAcc = GetCell(row, 4),
                    Y = GetCell(row, 5),
                    YVel = GetCell(row, 6),
                    YAcc = GetCell(row, 7),
                    YDeAcc = GetCell(row, 8),
                    Z = GetCell(row, 9),
                    ZVel = GetCell(row, 10),
                    ZAcc = GetCell(row, 11),
                    ZDeAcc = GetCell(row, 12),
                });
            }

            return result;
        }

        public DynamicTableDocument LoadRobotRecipeDynamic()
        {
            string path = Path.Combine(_dataDirectory, RobotRecipeFile);
            var rows = ReadCsvRows(path);
            var columns = new List<DynamicColumnSchema>
            {
                new DynamicColumnSchema { Key = "PointName", Title = "PointName", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = "Value", Title = "Value", IsReadOnly = false }
            };
            var rowList = new List<Dictionary<string, string>>(rows.Count);
            foreach (var row in rows)
            {
                rowList.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PointName"] = GetCell(row, 0),
                    ["Value"] = GetCell(row, 1),
                });
            }

            return new DynamicTableDocument
            {
                Title = "Robot",
                Columns = columns,
                Rows = rowList,
            };
        }

        public IReadOnlyList<RobotPointRecord> LoadRobotRecipe()
        {
            string path = Path.Combine(_dataDirectory, RobotRecipeFile);
            var rows = ReadCsvRows(path);
            var result = new List<RobotPointRecord>(rows.Count);
            foreach (var row in rows)
            {
                result.Add(new RobotPointRecord
                {
                    PointName = GetCell(row, 0),
                    Value = GetCell(row, 1),
                });
            }

            return result;
        }

        public IReadOnlyList<LightChannelRecord> LoadLightRecipe()
        {
            string path = Path.Combine(_dataDirectory, LightRecipeFile);
            var rows = ReadCsvRows(path);
            var result = new List<LightChannelRecord>(rows.Count);
            foreach (var row in rows)
            {
                result.Add(new LightChannelRecord
                {
                    ColourCode = GetCell(row, 0),
                    Colour = GetCell(row, 1),
                    Value = GetCell(row, 2),
                });
            }

            return result;
        }

        public RecipeSaveResult SaveGantryRecipe(string moduleName, IReadOnlyList<GantryPositionRecord> rows)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                throw new ArgumentException("Module name is required.", nameof(moduleName));
            }

            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            string path = GetGantryRecipePath(moduleName);
            string[] existingLines = ReadCsvLines(path);
            string header = existingLines.Length > 0
                ? existingLines[0]
                : "Position,X,X-Vel (mm),X-Axis Acc (mm/s),X-Axis De-Acc,Y,Y-Vel (mm),Y-Axis Acc (mm/s),Y-Axis De-acc,Z,Z-Vel (mm),Z-Axis Acc (mm/s),Z-Axis De-acc";

            var newRows = rows.Select(r => new[]
            {
                r.Position, r.X, r.XVel, r.XAcc, r.XDeAcc, r.Y, r.YVel, r.YAcc, r.YDeAcc, r.Z, r.ZVel, r.ZAcc, r.ZDeAcc
            }).ToList();

            return SaveCsv(path, header, newRows);
        }

        public RecipeSaveResult SaveRobotRecipeDynamic(DynamicTableDocument document)
        {
            if (document == null || document.Rows == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            string path = Path.Combine(_dataDirectory, RobotRecipeFile);
            string header = string.Join(",", document.Columns.Select(c => c.Title));
            var newRows = document.Rows.Select(r => document.Columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();
            return SaveCsv(path, header, newRows);
        }

        public RecipeSaveResult SaveRobotRecipe(IReadOnlyList<RobotPointRecord> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            var doc = LoadRobotRecipeDynamic();
            string keyName = doc.Columns.Count > 0 ? doc.Columns[0].Key : "PointName";
            string valName = doc.Columns.Count > 1 ? doc.Columns[1].Key : "Value";

            var newRows = new List<Dictionary<string, string>>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var existing = i < doc.Rows.Count ? new Dictionary<string, string>(doc.Rows[i], StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                existing[keyName] = rows[i].PointName;
                existing[valName] = rows[i].Value;
                newRows.Add(existing);
            }

            var newDoc = new DynamicTableDocument
            {
                Title = doc.Title,
                Columns = doc.Columns,
                Rows = newRows,
            };

            return SaveRobotRecipeDynamic(newDoc);
        }

        public RecipeSaveResult SaveLightRecipe(IReadOnlyList<LightChannelRecord> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            string path = Path.Combine(_dataDirectory, LightRecipeFile);
            string[] existingLines = ReadCsvLines(path);
            string header = existingLines.Length > 0 ? existingLines[0] : "Colour Code,Colour,Value";
            var newRows = rows.Select(r => new[] { r.ColourCode, r.Colour, r.Value }).ToList();
            return SaveCsv(path, header, newRows);
        }

        private RecipeSaveResult SaveCsv(string csvPath, string header, List<string[]> newRows)
        {
            string[] existingLines = ReadCsvLines(csvPath);
            var existingRows = ParseRows(existingLines.Skip(1));

            string newContent = BuildCsvContent(header, newRows);
            string existingContent = string.Join(Environment.NewLine, existingLines);

            if (Normalize(existingContent).Equals(Normalize(newContent), StringComparison.Ordinal))
            {
                return RecipeSaveResult.NoChanges("No data modified !");
            }

            File.WriteAllText(csvPath, newContent, Encoding.UTF8);
            AppendEditLog(Path.GetFileName(csvPath), SplitHeader(header), existingRows, newRows);
            return RecipeSaveResult.Saved("Data saved successfully !");
        }

        private void AppendEditLog(string sourceFile, string[] headers, List<string[]> oldRows, List<string[]> newRows)
        {
            var changes = CollectChanges(headers, oldRows, newRows);
            if (changes.Count == 0)
            {
                return;
            }

            string logPath = Path.Combine(_dataDirectory, EditLogFile);
            bool writeHeader = !File.Exists(logPath);
            using (var writer = new StreamWriter(logPath, true, Encoding.UTF8))
            {
                if (writeHeader)
                {
                    writer.WriteLine("Timestamp,User,Mode,SourceFile,Row,Column,OldValue,NewValue");
                }

                string user = string.IsNullOrWhiteSpace(_appSession.UserName) ? "Unknown" : _appSession.UserName;
                string mode = string.IsNullOrWhiteSpace(_appSession.Role) ? "Unknown" : _appSession.Role;
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                foreach (var change in changes)
                {
                    writer.WriteLine(string.Join(",",
                        EscapeCsv(timestamp),
                        EscapeCsv(user),
                        EscapeCsv(mode),
                        EscapeCsv(sourceFile),
                        EscapeCsv(change.RowIndex.ToString()),
                        EscapeCsv(change.ColumnName),
                        EscapeCsv(change.OldValue),
                        EscapeCsv(change.NewValue)));
                }
            }
        }

        private static List<RecipeChange> CollectChanges(string[] headers, List<string[]> oldRows, List<string[]> newRows)
        {
            var changes = new List<RecipeChange>();
            int maxRows = Math.Max(oldRows.Count, newRows.Count);
            for (int rowIndex = 0; rowIndex < maxRows; rowIndex++)
            {
                string[] oldRow = rowIndex < oldRows.Count ? oldRows[rowIndex] : Array.Empty<string>();
                string[] newRow = rowIndex < newRows.Count ? newRows[rowIndex] : Array.Empty<string>();
                int maxCols = Math.Max(headers.Length, Math.Max(oldRow.Length, newRow.Length));
                for (int colIndex = 0; colIndex < maxCols; colIndex++)
                {
                    string oldValue = GetCell(oldRow, colIndex);
                    string newValue = GetCell(newRow, colIndex);
                    if (oldValue.Equals(newValue, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    changes.Add(new RecipeChange
                    {
                        RowIndex = rowIndex + 1,
                        ColumnName = colIndex < headers.Length ? headers[colIndex] : "Column" + (colIndex + 1),
                        OldValue = oldValue,
                        NewValue = newValue,
                    });
                }
            }

            return changes;
        }

        private static string BuildCsvContent(string header, List<string[]> rows)
        {
            var builder = new StringBuilder();
            builder.AppendLine(header);
            foreach (var row in rows)
            {
                builder.AppendLine(string.Join(",", row.Select(EscapeCsv)));
            }

            return builder.ToString();
        }

        private static string[] SplitHeader(string header)
        {
            return (header ?? string.Empty).Split(',');
        }

        private static List<string[]> ReadCsvRows(string csvPath)
        {
            var lines = ReadCsvLines(csvPath);
            return ParseRows(lines.Skip(1));
        }

        private static List<string[]> ParseRows(IEnumerable<string> lines)
        {
            return lines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Split(','))
                .ToList();
        }

        private static string[] ReadCsvLines(string csvPath)
        {
            if (!File.Exists(csvPath))
            {
                throw new FileNotFoundException("Recipe file not found.", csvPath);
            }

            return File.ReadAllLines(csvPath);
        }

        private string GetGantryRecipePath(string moduleName)
        {
            return Path.Combine(_dataDirectory, moduleName + "Receipe_Param.csv");
        }

        private XElement LoadGantryStructureRoot()
        {
            string xmlPath = Path.Combine(_dataDirectory, GantryStructureFile);
            if (!File.Exists(xmlPath))
            {
                throw new FileNotFoundException("Gantry structure file not found.", xmlPath);
            }

            var root = XDocument.Load(xmlPath).Root;
            if (root == null)
            {
                throw new InvalidDataException("Gantry structure XML has no root element.");
            }

            return root;
        }

        private static IReadOnlyList<string> ReadPriority(XElement moduleElement, string priorityElementName)
        {
            var priorityElement = moduleElement.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals(priorityElementName, StringComparison.OrdinalIgnoreCase));
            if (priorityElement == null)
            {
                return Array.Empty<string>();
            }

            return priorityElement.Elements()
                .Select(e => (e.Value ?? string.Empty).Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
        }

        private static string ReadAxisName(XElement axisNode)
        {
            var axisNameElement = axisNode.Elements()
                .FirstOrDefault(e => e.Name.LocalName.IndexOf("Axis", StringComparison.OrdinalIgnoreCase) >= 0
                    && e.Name.LocalName.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0);
            if (axisNameElement != null)
            {
                return axisNameElement.Value.Trim();
            }

            var values = axisNode.Elements().Select(e => (e.Value ?? string.Empty).Trim()).ToList();
            return values.Count > 2 ? values[2] : string.Empty;
        }

        private static bool TryReadHomingSpeed(XElement axisNode, out double speed)
        {
            speed = 0d;
            var speedElement = axisNode.Elements()
                .FirstOrDefault(e => e.Name.LocalName.IndexOf("Homing", StringComparison.OrdinalIgnoreCase) >= 0
                    && e.Name.LocalName.IndexOf("Speed", StringComparison.OrdinalIgnoreCase) >= 0);
            if (speedElement != null && TryParseNumber(speedElement.Value.Trim(), out speed))
            {
                return true;
            }

            var values = axisNode.Elements().Select(e => (e.Value ?? string.Empty).Trim()).ToList();
            return values.Count > 8 && TryParseNumber(values[8], out speed);
        }

        private static bool TryParseNumber(string value, out double parsed)
        {
            return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed)
                || double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out parsed);
        }

        private static string Normalize(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd();
        }

        private static string EscapeCsv(string value)
        {
            string safe = value ?? string.Empty;
            if (safe.Contains("\""))
            {
                safe = safe.Replace("\"", "\"\"");
            }

            if (safe.Contains(",") || safe.Contains("\"") || safe.Contains("\n") || safe.Contains("\r"))
            {
                return "\"" + safe + "\"";
            }

            return safe;
        }

        private static string GetCell(string[] row, int index)
        {
            return (row != null && index >= 0 && index < row.Length) ? row[index] : string.Empty;
        }

        private sealed class RecipeChange
        {
            public int RowIndex { get; set; }
            public string ColumnName { get; set; } = string.Empty;
            public string OldValue { get; set; } = string.Empty;
            public string NewValue { get; set; } = string.Empty;
        }
    }
}
