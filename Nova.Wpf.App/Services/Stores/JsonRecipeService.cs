using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Master.Configuration;
using Master.Configuration.Model;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    public sealed class JsonRecipeService : IRecipeService
    {
        private const string SettingsFolder = "Settings";

        private const string RobotRecipeJsonFile = "RobotRecipe.json";
        private const string RobotRecipeCsvFile = "RoboReceipe_Param.csv";
        private const string CncRecipeJsonFile = "CncRecipe.json";
        private const string EditLogFile = "Recipe_Edit_Log.csv";

        private readonly string _dataDirectory;
        private readonly IAppSession _appSession;

        public event EventHandler RecipeChanged;

        public JsonRecipeService(string dataDirectory, IAppSession appSession)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                throw new ArgumentException("Data directory cannot be empty.", nameof(dataDirectory));
            }

            _dataDirectory = dataDirectory;
            _appSession = appSession ?? throw new ArgumentNullException(nameof(appSession));
        }

        private void WriteTextDual(string jsonPath, string relativeFileName, string content)
        {
            File.WriteAllText(jsonPath, content, Encoding.UTF8);

            try
            {
                string sourceDir = FindSourceProjectDataDirectory();
                if (!string.IsNullOrEmpty(sourceDir))
                {
                    string sourcePath = Path.Combine(sourceDir, relativeFileName);
                    string targetDir = Path.GetDirectoryName(sourcePath);
                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                    File.WriteAllText(sourcePath, content, Encoding.UTF8);
                }
            }
            catch
            {
                // Ignore locked dev source file
            }

            RecipeChanged?.Invoke(this, EventArgs.Empty);
        }

        private static string FindSourceProjectDataDirectory()
        {
            try
            {
                string current = AppDomain.CurrentDomain.BaseDirectory;
                DirectoryInfo dir = new DirectoryInfo(current);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "Nova.Wpf.App", "Data");
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                    dir = dir.Parent;
                }
            }
            catch
            {
            }
            return null;
        }


        // ==================================================================
        // Gantry recipe
        //
        // Data\Gantry.json lists the gantries: each one's Name and PlcGantry (the PLC's
        // E_Recipe number), and optionally its Axes with each servo's Type (X, Y, Z, R: the
        // jog button that drives it) and move order. A gantry's recipe is its own file in the
        // data folder, named after it: {Name}.xlsx, or {Name}.csv (GantryRecipeFile). Its
        // column groups are the gantry's servos, in the order the PLC numbers its axes
        // (E_Servo of that gantry); its rows are the positions. The Gantry tab shows that file
        // and Save writes the edited values back into it.
        // ==================================================================

        /// <summary>The modules of Gantry.json, named; an empty list when there is no such file.</summary>
        private List<GantryConfiguration> ReadGantryModules()
        {
            var root = MachineConfiguration.LoadGantry(_dataDirectory);
            return (root?.Gantry ?? new List<GantryConfiguration>())
                .Where(g => g != null && !string.IsNullOrWhiteSpace(g.Name))
                .ToList();
        }

        private GantryConfiguration FindGantryModule(string moduleName)
        {
            string wanted = (moduleName ?? string.Empty).Trim();
            return ReadGantryModules().FirstOrDefault(g => string.Equals(g.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The gantry's recipe file as it is now, or null with the reason when it has none or it cannot be read.</summary>
        private GantryRecipeTable ReadGantryFile(string moduleName, out string problem)
        {
            problem = null;
            string name = (moduleName ?? string.Empty).Trim();
            string path = GantryRecipeFile.Find(_dataDirectory, name);
            if (path == null)
            {
                problem = "There is no recipe file for " + name + ": " + GantryRecipeFile.Expected(_dataDirectory, name) + ".";
                return null;
            }

            try
            {
                return GantryRecipeFile.Read(path);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
            {
                problem = "The recipe of " + name + " cannot be read from " + path + ": " + ex.Message;
                return null;
            }
        }

        /// <summary>Axis names by priority: 1 first, 0 (not given) after the numbered ones, ties in file order.</summary>
        private static List<string> ByPriority(List<GantryAxisDefinition> axes, Func<GantryAxisDefinition, int> priority)
        {
            return axes
                .Select((axis, index) => new { axis, index })
                .OrderBy(x => priority(x.axis) > 0 ? priority(x.axis) : int.MaxValue)
                .ThenBy(x => x.index)
                .Select(x => x.axis.Name)
                .ToList();
        }

        public IReadOnlyList<string> GetGantryModules()
        {
            return ReadGantryModules().Select(g => g.Name.Trim()).ToList();
        }

        /// <summary>
        /// The gantry's axes are the servos its recipe file heads its column groups with, in
        /// that order (the PLC's axis numbers). A servo Gantry.json also lists under Axes takes
        /// its Type and priorities from there; one it does not list has no Type (no jog
        /// button) and moves in column order. Without a readable file the axes are the ones
        /// Gantry.json lists, so the jog pad still works.
        /// </summary>
        public GantryModuleDefinition GetGantryModuleDefinition(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                throw new ArgumentException("Module name is required.", nameof(moduleName));
            }

            var module = FindGantryModule(moduleName);
            var listed = (module?.Axes ?? new List<_Axis>()).Where(a => a != null && !string.IsNullOrWhiteSpace(a.Name)).ToList();
            GantryRecipeTable file = ReadGantryFile(moduleName, out _);
            IEnumerable<string> names = file != null ? file.Axes : listed.Select(a => a.Name.Trim());

            var axes = new List<GantryAxisDefinition>();
            foreach (string name in names)
            {
                _Axis given = listed.FirstOrDefault(a => string.Equals(a.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
                string type = (given?.Type ?? string.Empty).Trim().ToUpperInvariant();
                if (!MachineConfiguration.GantryAxisTypes.Contains(type) || axes.Any(a => a.Type == type))
                {
                    type = string.Empty;
                }

                axes.Add(new GantryAxisDefinition
                {
                    Name = name,
                    Type = type,
                    MovementPriority = given?.MovementPriority ?? 0,
                    SafePositionPriority = given?.SafePositionPriority ?? 0,
                });
            }

            return new GantryModuleDefinition
            {
                ModuleName = moduleName.Trim(),
                Axes = axes,
                MovementPriority = ByPriority(axes, a => a.MovementPriority),
                SafePositionPriority = ByPriority(axes, a => a.SafePositionPriority),
                PlcGantry = module?.PlcGantry ?? 0,
            };
        }

        /// <summary>The positions of the gantry's recipe file, in its row order. Throws when it has no readable file.</summary>
        public IReadOnlyList<GantryPosition> LoadGantryPositions(string moduleName)
        {
            GantryRecipeTable file = ReadGantryFile(moduleName, out string problem);
            if (file == null)
            {
                throw new InvalidOperationException(problem);
            }

            return file.Positions;
        }

        private static List<DynamicColumnSchema> GantryColumns(GantryModuleDefinition definition)
        {
            var columns = new List<DynamicColumnSchema>
            {
                new DynamicColumnSchema { Key = GantryRecipeColumns.Position, Title = "Position", IsReadOnly = true, IsIdentifier = true, DataType = ColumnDataType.Text },
            };

            foreach (var axis in definition.Axes)
            {
                columns.Add(new DynamicColumnSchema { Key = GantryRecipeColumns.Value(axis.Name), Title = axis.Name + " Position", DataType = ColumnDataType.Numeric });
                columns.Add(new DynamicColumnSchema { Key = GantryRecipeColumns.Speed(axis.Name), Title = axis.Name + " Speed", DataType = ColumnDataType.NonNegative });
                columns.Add(new DynamicColumnSchema { Key = GantryRecipeColumns.Acc(axis.Name), Title = axis.Name + " Acc", DataType = ColumnDataType.NonNegative });
                columns.Add(new DynamicColumnSchema { Key = GantryRecipeColumns.Dec(axis.Name), Title = axis.Name + " Dec", DataType = ColumnDataType.NonNegative });
            }

            return columns;
        }

        private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>
        /// What is wrong with the gantry's servos, for the warning the tab shows: a column group
        /// headed by a name that is not a servo in Servos.json, and a servo another gantry's
        /// recipe file has too (the PLC write refuses that, as the two would fight over it).
        /// </summary>
        private List<string> GantryAxisProblems(string moduleName, IReadOnlyList<string> axes)
        {
            var problems = new List<string>();
            var servos = MachineConfiguration.ReadServos(_dataDirectory) ?? new List<ServoConfiguration>();
            List<string> unknown = axes
                .Where(a => !servos.Any(s => s != null && string.Equals(s.EnumName, a, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (unknown.Count > 0)
            {
                problems.Add("Not a servo in Data\\" + MachineConfiguration.ServosFile + " (no EnumName of that name), so it cannot be jogged or moved: "
                    + string.Join(", ", unknown) + ".");
            }

            foreach (string other in GetGantryModules())
            {
                if (string.Equals(other, moduleName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                GantryRecipeTable otherFile = ReadGantryFile(other, out _);
                List<string> shared = otherFile == null
                    ? new List<string>()
                    : axes.Where(a => otherFile.Axes.Any(o => string.Equals(o, a, StringComparison.OrdinalIgnoreCase))).ToList();
                if (shared.Count > 0)
                {
                    problems.Add(string.Join(", ", shared) + " is also in the recipe of " + other + " (" + Path.GetFileName(otherFile.Path)
                        + "). One of the two files names the wrong servo; neither recipe is written to the PLC until it is corrected.");
                }
            }

            return problems;
        }

        public DynamicTableDocument LoadGantryRecipeDynamic(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                throw new ArgumentException("Module name is required.", nameof(moduleName));
            }

            var definition = GetGantryModuleDefinition(moduleName);
            GantryRecipeTable file = ReadGantryFile(moduleName, out string problem);
            var warnings = new List<string>();
            if (file == null)
            {
                warnings.Add(problem);
            }
            else
            {
                warnings.AddRange(GantryAxisProblems(moduleName, file.Axes));
            }

            var rows = new List<Dictionary<string, string>>();
            foreach (var position in file?.Positions ?? new List<GantryPosition>())
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [GantryRecipeColumns.Position] = position.Name,
                };

                foreach (var axis in definition.Axes)
                {
                    position.Axes.TryGetValue(axis.Name, out var move);
                    move = move ?? new GantryAxisMove();
                    row[GantryRecipeColumns.Value(axis.Name)] = Format(move.Value);
                    row[GantryRecipeColumns.Speed(axis.Name)] = Format(move.Speed);
                    row[GantryRecipeColumns.Acc(axis.Name)] = Format(move.Acceleration);
                    row[GantryRecipeColumns.Dec(axis.Name)] = Format(move.Deceleration);
                }

                rows.Add(row);
            }

            return new DynamicTableDocument
            {
                Title = moduleName.Trim(),
                Columns = GantryColumns(definition),
                Rows = rows,
                HasMissingColumnSchema = warnings.Count > 0,
                MissingSchemaMessage = string.Join(Environment.NewLine + Environment.NewLine, warnings),
            };
        }

        /// <summary>
        /// Writes the table into the gantry's recipe file: every row's values, in the file's
        /// own column groups. Checked whole first, so a bad cell saves nothing; a file that
        /// cannot be replaced (open in Excel) saves nothing either and says so.
        /// </summary>
        public RecipeSaveResult SaveGantryRecipeDynamic(string moduleName, DynamicTableDocument document)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                throw new ArgumentException("Module name is required.", nameof(moduleName));
            }

            if (document == null || document.Rows == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            GantryRecipeTable file = ReadGantryFile(moduleName, out string problem);
            if (file == null)
            {
                throw new InvalidOperationException(problem + " Nothing was saved.");
            }

            var oldDoc = LoadGantryRecipeDynamic(moduleName);

            // Every cell is checked before anything changes, so a bad cell saves nothing.
            var positions = new List<GantryPosition>();
            foreach (var row in document.Rows)
            {
                string name = Cell(row, GantryRecipeColumns.Position);
                if (name.Length == 0)
                {
                    continue;
                }

                var position = new GantryPosition { Name = name };
                foreach (string axis in file.Axes)
                {
                    position.Axes[axis] = new GantryAxisMove
                    {
                        Value = GantryNumber(name, row, GantryRecipeColumns.Value(axis)),
                        Speed = GantryNumber(name, row, GantryRecipeColumns.Speed(axis)),
                        Acceleration = GantryNumber(name, row, GantryRecipeColumns.Acc(axis)),
                        Deceleration = GantryNumber(name, row, GantryRecipeColumns.Dec(axis)),
                    };
                }

                positions.Add(position);
            }

            if (!GantryChanged(file, positions))
            {
                return RecipeSaveResult.NoChanges("No data modified !");
            }

            string fileName = Path.GetFileName(file.Path);
            try
            {
                GantryRecipeFile.Write(file.Path, file.Axes, positions);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new InvalidOperationException(fileName + " could not be written (" + ex.Message + "). If it is open in Excel, close it and Save again. Nothing was saved.", ex);
            }

            RecipeChanged?.Invoke(this, EventArgs.Empty);

            var columns = oldDoc.Columns;
            string[] headers = columns.Select(c => c.Title).ToArray();
            var oldRowArray = oldDoc.Rows.Select(r => columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();
            var newRowArray = document.Rows.Select(r => columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();
            AppendEditLog(fileName + " (" + moduleName.Trim() + ")", headers, oldRowArray, newRowArray);

            return RecipeSaveResult.Saved("Data saved successfully to " + fileName + " !");
        }

        /// <summary>True when the positions differ from the file's: a name, an order, a count or a value.</summary>
        private static bool GantryChanged(GantryRecipeTable file, List<GantryPosition> positions)
        {
            if (file.Positions.Count != positions.Count)
            {
                return true;
            }

            for (int p = 0; p < positions.Count; p++)
            {
                GantryPosition before = file.Positions[p];
                GantryPosition after = positions[p];
                if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal))
                {
                    return true;
                }

                foreach (string axis in file.Axes)
                {
                    before.Axes.TryGetValue(axis, out var a);
                    after.Axes.TryGetValue(axis, out var b);
                    a = a ?? new GantryAxisMove();
                    b = b ?? new GantryAxisMove();
                    if (a.Value != b.Value || a.Speed != b.Speed || a.Acceleration != b.Acceleration || a.Deceleration != b.Deceleration)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string Cell(Dictionary<string, string> row, string key)
        {
            return row != null && row.TryGetValue(key, out var value) && value != null ? value.Trim() : string.Empty;
        }

        /// <summary>The cell as a number; a blank cell is 0. Throws for anything else that is not a number.</summary>
        private static double GantryNumber(string position, Dictionary<string, string> row, string key)
        {
            string raw = Cell(row, key);
            if (raw.Length == 0)
            {
                return 0d;
            }

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return value;
            }

            throw new InvalidOperationException("'" + position + "' " + key + " is '" + raw + "', not a number. Nothing was saved.");
        }

        /// <summary>
        /// The speed ceiling of each servo, its Velocity in Data\Servos.json (the same value the
        /// Manual axis tab limits to). A servo with no Velocity set has no entry, so it is not limited.
        /// </summary>
        public IReadOnlyDictionary<string, double> GetAxisSpeedLimits()
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var servo in MachineConfiguration.ReadServos(_dataDirectory) ?? new List<ServoConfiguration>())
            {
                if (servo == null || string.IsNullOrWhiteSpace(servo.EnumName) || servo.Velocity <= 0)
                {
                    continue;
                }

                result[servo.EnumName.Trim()] = servo.Velocity;
            }

            return result;
        }

        public DynamicTableDocument LoadRobotRecipeDynamic()
        {
            string jsonPath = Path.Combine(_dataDirectory, RobotRecipeJsonFile);
            if (File.Exists(jsonPath))
            {
                try
                {
                    string jsonContent = File.ReadAllText(jsonPath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(jsonContent))
                    {
                        var parsedDoc = ParseDynamicJson(jsonContent, "Robot");
                        if (parsedDoc != null && parsedDoc.Columns.Count > 0 && parsedDoc.Rows.Count > 0)
                        {
                            return parsedDoc;
                        }
                    }
                }
                catch
                {
                    // Fallback to CSV
                }
            }

            // Fallback to CSV
            string csvPath = Path.Combine(_dataDirectory, RobotRecipeCsvFile);
            var rows = ReadCsvRows(csvPath);
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
            var doc = LoadRobotRecipeDynamic();
            var result = new List<RobotPointRecord>(doc.Rows.Count);
            string keyName = doc.Columns.Count > 0 ? doc.Columns[0].Key : "PointName";
            string valName = doc.Columns.Count > 1 ? doc.Columns[1].Key : "Value";
            foreach (var row in doc.Rows)
            {
                result.Add(new RobotPointRecord
                {
                    PointName = row.TryGetValue(keyName, out var p) ? p : string.Empty,
                    Value = row.TryGetValue(valName, out var v) ? v : "0",
                });
            }

            return result;
        }

        public RecipeSaveResult SaveRobotRecipeDynamic(DynamicTableDocument document)
        {
            if (document == null || document.Rows == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var oldDoc = LoadRobotRecipeDynamic();
            string jsonPath = Path.Combine(_dataDirectory, RobotRecipeJsonFile);

            // The grid hands back only the columns. A row's other properties - its PLC mapping
            // (PlcMember, Min, Max), which TwincatRecipeService reads - are kept from the file,
            // matched by the point name.
            string pointKey = document.Columns.Count > 0 ? document.Columns[0].Key : "PointName";
            var columnKeys = new HashSet<string>(document.Columns.Select(c => c.Key), StringComparer.OrdinalIgnoreCase);
            var oldRows = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var oldRow in oldDoc.Rows)
            {
                string point = oldRow.TryGetValue(pointKey, out var p) ? p : string.Empty;
                if (!oldRows.ContainsKey(point))
                {
                    oldRows[point] = oldRow;
                }
            }

            var jRows = new JArray();
            foreach (var row in document.Rows)
            {
                var jObj = new JObject();
                foreach (var col in document.Columns)
                {
                    jObj[col.Key] = row.TryGetValue(col.Key, out var val) ? val : string.Empty;
                }

                string point = row.TryGetValue(pointKey, out var id) ? id : string.Empty;
                if (oldRows.TryGetValue(point, out var kept))
                {
                    foreach (var property in kept.Where(kvp => !columnKeys.Contains(kvp.Key)))
                    {
                        jObj[property.Key] = property.Value;
                    }
                }

                jRows.Add(jObj);
            }

            // Written whole - Title, Columns and Rows - so the schema survives a save.
            var jColumns = new JArray();
            foreach (var col in document.Columns)
            {
                var jCol = new JObject { ["Key"] = col.Key, ["Title"] = col.Title };
                if (col.IsReadOnly)
                {
                    jCol["IsReadOnly"] = true;
                }
                if (col.IsIdentifier)
                {
                    jCol["IsIdentifier"] = true;
                }
                jCol["DataType"] = col.DataType.ToString();
                if (!col.ValidationEnabled)
                {
                    jCol["ValidationEnabled"] = false;
                }
                jColumns.Add(jCol);
            }

            var jDoc = new JObject
            {
                ["Title"] = document.Title,
                ["Columns"] = jColumns,
                ["Rows"] = jRows,
            };

            string newJson = jDoc.ToString(Formatting.Indented);
            string oldJson = File.Exists(jsonPath) ? File.ReadAllText(jsonPath, Encoding.UTF8) : string.Empty;

            if (!string.IsNullOrEmpty(oldJson) && Normalize(oldJson).Equals(Normalize(newJson), StringComparison.Ordinal))
            {
                return RecipeSaveResult.NoChanges("No data modified !");
            }

            WriteTextDual(jsonPath, RobotRecipeJsonFile, newJson);

            string[] headers = document.Columns.Select(c => c.Title).ToArray();
            var oldRowArray = oldDoc.Rows.Select(r => document.Columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();
            var newRowArray = document.Rows.Select(r => document.Columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();

            AppendEditLog(Path.GetFileName(jsonPath), headers, oldRowArray, newRowArray);
            return RecipeSaveResult.Saved("Data saved successfully !");
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

        /// <summary>Data\CncRecipe.json: { "Channels": [ { Name, PlcChannel, Axes: [ { Axis, Servo, Position } ] } ] }.</summary>
        private sealed class CncRecipeFile
        {
            public List<CncChannelRecipe> Channels { get; set; } = new List<CncChannelRecipe>();
        }

        /// <summary>Throws InvalidDataException for a file that is not a CNC recipe; no file is no channels.</summary>
        public IReadOnlyList<CncChannelRecipe> LoadCncRecipe()
        {
            string jsonPath = Path.Combine(_dataDirectory, CncRecipeJsonFile);
            if (!File.Exists(jsonPath))
            {
                return new CncChannelRecipe[0];
            }

            try
            {
                var file = JsonConvert.DeserializeObject<CncRecipeFile>(File.ReadAllText(jsonPath, Encoding.UTF8));
                return (IReadOnlyList<CncChannelRecipe>)file?.Channels ?? new CncChannelRecipe[0];
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(CncRecipeJsonFile + " could not be read: " + ex.Message, ex);
            }
        }

        public RecipeSaveResult SaveCncRecipe(IReadOnlyList<CncChannelRecipe> channels)
        {
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            string jsonPath = Path.Combine(_dataDirectory, CncRecipeJsonFile);
            string oldJson = File.Exists(jsonPath) ? File.ReadAllText(jsonPath, Encoding.UTF8) : string.Empty;
            IReadOnlyList<CncChannelRecipe> oldChannels = LoadCncRecipe();

            string newJson = JsonConvert.SerializeObject(new CncRecipeFile { Channels = channels.ToList() }, Formatting.Indented);
            if (!string.IsNullOrEmpty(oldJson) && Normalize(oldJson).Equals(Normalize(newJson), StringComparison.Ordinal))
            {
                return RecipeSaveResult.NoChanges("No data modified !");
            }

            WriteTextDual(jsonPath, CncRecipeJsonFile, newJson);

            // One edit-log row per channel position: "Channel 1.Home", then its value on each axis.
            string[] headers = new[] { "Position" }.Concat(CncChannelRecipe.AxisNames).ToArray();
            AppendEditLog(CncRecipeJsonFile, headers, CncLogRows(oldChannels), CncLogRows(channels));
            return RecipeSaveResult.Saved("Data saved successfully !");
        }

        private static List<string[]> CncLogRows(IReadOnlyList<CncChannelRecipe> channels)
        {
            return channels
                .SelectMany(channel => (channel.Positions ?? new List<CncPositionRecipe>()).Select(position =>
                    new[] { channel.Name + "." + position.Name }
                        .Concat(CncChannelRecipe.AxisNames.Select(axis =>
                            position.Values != null && position.Values.TryGetValue(axis, out double value)
                                ? value.ToString(CultureInfo.InvariantCulture)
                                : string.Empty))
                        .ToArray()))
                .ToList();
        }

        private static DynamicTableDocument ParseDynamicJson(string jsonContent, string defaultTitle, string[] identifierKeys = null)
        {
            var token = JToken.Parse(jsonContent);
            if (token is JArray jArray)
            {
                return ParseDynamicJArray(jArray, defaultTitle, identifierKeys);
            }
            else if (token is JObject jObj)
            {
                string title = jObj["Title"]?.ToString() ?? defaultTitle;
                var columns = new List<DynamicColumnSchema>();
                bool hasExplicitColumns = jObj["Columns"] is JArray colsArray && colsArray.Count > 0;

                if (hasExplicitColumns)
                {
                    var explicitCols = (JArray)jObj["Columns"];
                    foreach (var colToken in explicitCols)
                    {
                        if (colToken is JObject colObj)
                        {
                            string key = colObj["Key"]?.ToString() ?? string.Empty;
                            string colTitle = colObj["Title"]?.ToString() ?? key;
                            bool isReadOnly = colObj["IsReadOnly"] != null && (bool)colObj["IsReadOnly"];
                            bool isIdentifier = colObj["IsIdentifier"] != null && (bool)colObj["IsIdentifier"];
                            bool validationEnabled = colObj["ValidationEnabled"] == null || (bool)colObj["ValidationEnabled"];
                            ColumnDataType dataType = ColumnDataType.Text;
                            if (colObj["DataType"] != null && Enum.TryParse<ColumnDataType>(colObj["DataType"].ToString(), true, out var parsedType))
                            {
                                dataType = parsedType;
                            }
                            columns.Add(new DynamicColumnSchema
                            {
                                Key = key,
                                Title = colTitle,
                                IsReadOnly = isReadOnly,
                                IsIdentifier = isIdentifier,
                                DataType = dataType,
                                ValidationEnabled = validationEnabled
                            });
                        }
                    }
                }

                var rows = new List<Dictionary<string, string>>();
                if (jObj["Rows"] is JArray rowsArray)
                {
                    foreach (var rowToken in rowsArray)
                    {
                        if (rowToken is JObject rowObj)
                        {
                            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var prop in rowObj.Properties())
                            {
                                dict[prop.Name] = prop.Value?.ToString() ?? string.Empty;
                            }
                            rows.Add(dict);
                        }
                    }
                }

                if (!hasExplicitColumns && rows.Count > 0 && jObj["Rows"] is JArray fallbackRows)
                {
                    return ParseDynamicJArray(fallbackRows, title, identifierKeys);
                }

                return new DynamicTableDocument
                {
                    Title = title,
                    HasMissingColumnSchema = !hasExplicitColumns,
                    MissingSchemaMessage = !hasExplicitColumns ? "Configuration file '" + title + "' is missing an explicit 'Columns' schema definition. Please add explicit column definitions ('Key', 'Title', 'IsReadOnly', 'IsIdentifier', 'DataType', 'ValidationEnabled') to prevent schema ambiguity." : string.Empty,
                    Columns = columns,
                    Rows = rows
                };
            }

            return new DynamicTableDocument { Title = defaultTitle };
        }

        private static DynamicTableDocument ParseDynamicJArray(JArray jArray, string defaultTitle, string[] identifierKeys = null)
        {
            var discoveredKeys = new List<string>();
            var rows = new List<Dictionary<string, string>>();

            foreach (var item in jArray)
            {
                if (item is JObject rowObj)
                {
                    var rowDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in rowObj.Properties())
                    {
                        if (!discoveredKeys.Contains(prop.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            discoveredKeys.Add(prop.Name);
                        }
                        rowDict[prop.Name] = prop.Value?.ToString() ?? string.Empty;
                    }
                    rows.Add(rowDict);
                }
            }

            var columns = new List<DynamicColumnSchema>();
            for (int i = 0; i < discoveredKeys.Count; i++)
            {
                string key = discoveredKeys[i];
                bool isIdent = identifierKeys != null && identifierKeys.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (identifierKeys == null && i == 0) isIdent = true;

                columns.Add(new DynamicColumnSchema
                {
                    Key = key,
                    Title = key,
                    IsReadOnly = isIdent,
                    IsIdentifier = isIdent,
                    DataType = ColumnDataType.Text,
                    ValidationEnabled = true
                });
            }

            return new DynamicTableDocument
            {
                Title = defaultTitle,
                HasMissingColumnSchema = true,
                MissingSchemaMessage = "Configuration file '" + defaultTitle + "' is missing an explicit 'Columns' schema definition. Please add explicit column definitions ('Key', 'Title', 'IsReadOnly', 'IsIdentifier', 'DataType', 'ValidationEnabled') to prevent schema ambiguity.",
                Columns = columns,
                Rows = rows
            };
        }

        private static List<string[]> ReadCsvRows(string path)
        {
            string[] lines = ReadCsvLines(path);
            if (lines.Length <= 1)
            {
                return new List<string[]>(0);
            }

            return ParseRows(lines.Skip(1));
        }

        private static string[] ReadCsvLines(string path)
        {
            if (!File.Exists(path))
            {
                return new string[0];
            }

            return File.ReadAllLines(path, Encoding.UTF8)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();
        }

        private static List<string[]> ParseRows(IEnumerable<string> lines)
        {
            var result = new List<string[]>();
            foreach (var line in lines)
            {
                var parts = line.Split(',').Select(p => p.Trim()).ToArray();
                result.Add(parts);
            }

            return result;
        }

        private static string GetCell(string[] row, int index)
        {
            if (row == null || index < 0 || index >= row.Length)
            {
                return string.Empty;
            }

            return row[index];
        }

        private static string Normalize(string text)
        {
            if (text == null)
            {
                return string.Empty;
            }

            return text.Replace("\r\n", "\n").Trim();
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
                        EscapeCsv(change.RowIdentity),
                        EscapeCsv(change.Parameter),
                        EscapeCsv(change.OldValue),
                        EscapeCsv(change.NewValue)));
                }
            }
        }

        private static List<RecipeChange> CollectChanges(string[] headers, List<string[]> oldRows, List<string[]> newRows)
        {
            var result = new List<RecipeChange>();
            int count = Math.Max(oldRows.Count, newRows.Count);

            for (int r = 0; r < count; r++)
            {
                string[] oldRow = r < oldRows.Count ? oldRows[r] : null;
                string[] newRow = r < newRows.Count ? newRows[r] : null;
                string rowIdentity = GetCell(newRow, 0);
                if (string.IsNullOrWhiteSpace(rowIdentity))
                {
                    rowIdentity = GetCell(oldRow, 0);
                }

                if (string.IsNullOrWhiteSpace(rowIdentity))
                {
                    rowIdentity = "Row_" + (r + 1);
                }

                int colCount = Math.Max(headers.Length, Math.Max(oldRow != null ? oldRow.Length : 0, newRow != null ? newRow.Length : 0));
                for (int c = 1; c < colCount; c++)
                {
                    string parameter = c < headers.Length ? headers[c] : "Col_" + c;
                    string oldVal = GetCell(oldRow, c);
                    string newVal = GetCell(newRow, c);

                    if (!oldVal.Equals(newVal, StringComparison.Ordinal))
                    {
                        result.Add(new RecipeChange
                        {
                            RowIdentity = rowIdentity,
                            Parameter = parameter,
                            OldValue = oldVal,
                            NewValue = newVal,
                        });
                    }
                }
            }

            return result;
        }

        private static string EscapeCsv(string field)
        {
            if (field == null)
            {
                return string.Empty;
            }

            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }

            return field;
        }

        private sealed class RecipeChange
        {
            public string RowIdentity { get; set; }
            public string Parameter { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
        }
    }
}
