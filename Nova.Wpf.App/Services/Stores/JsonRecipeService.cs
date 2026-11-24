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
        // Gantry recipe, the V3 way
        //
        // Data\Gantry.json lists the gantry modules and the servos each is made of (with its
        // type, X/Y/Z/R, and the order it moves in). A module's recipe is not a file of its
        // own: it is the positions taught on its servos, each servo's ConfiguredPositions in
        // Data\Servos.json. The table has a row per position name found on any of the
        // module's servos (first-seen order) and a group of four columns per servo:
        // {servo}_Value, {servo}_Speed, {servo}_Acc, {servo}_Dec. Save writes each group
        // back into its servo's position of that name, adding it if the servo has none.
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

        /// <summary>
        /// The module's usable axes, in Gantry.json's order: a servo of Servos.json, typed X,
        /// Y, Z or R, and the first such axis of each type (MachineConfiguration.Load reports
        /// the rest at startup). The name is the servo's EnumName, its axis ComponentId.
        /// </summary>
        private static List<GantryAxisDefinition> UsableAxes(GantryConfiguration module, List<ServoConfiguration> servos)
        {
            var axes = new List<GantryAxisDefinition>();
            foreach (var axis in module?.Axes ?? new List<_Axis>())
            {
                string type = (axis?.Type ?? string.Empty).Trim().ToUpperInvariant();
                var servo = axis == null || string.IsNullOrWhiteSpace(axis.Name) ? null : FindServo(servos, axis.Name.Trim());
                if (servo == null
                    || !MachineConfiguration.GantryAxisTypes.Contains(type)
                    || axes.Any(a => a.Type == type))
                {
                    continue;
                }

                axes.Add(new GantryAxisDefinition
                {
                    Name = string.IsNullOrWhiteSpace(servo.EnumName) ? axis.Name.Trim() : servo.EnumName,
                    Type = type,
                    MovementPriority = axis.MovementPriority,
                    SafePositionPriority = axis.SafePositionPriority,
                });
            }

            return axes;
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

        public GantryModuleDefinition GetGantryModuleDefinition(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                throw new ArgumentException("Module name is required.", nameof(moduleName));
            }

            var module = FindGantryModule(moduleName);
            var axes = UsableAxes(module, MachineConfiguration.ReadServos(_dataDirectory) ?? new List<ServoConfiguration>());
            return new GantryModuleDefinition
            {
                ModuleName = moduleName.Trim(),
                Axes = axes,
                MovementPriority = ByPriority(axes, a => a.MovementPriority),
                SafePositionPriority = ByPriority(axes, a => a.SafePositionPriority),
                PlcGantry = module?.PlcGantry ?? 0,
            };
        }

        /// <summary>The servo of Servos.json an axis names: by EnumName, else by Name (as V3 matches).</summary>
        private static ServoConfiguration FindServo(List<ServoConfiguration> servos, string axisName)
        {
            return servos.FirstOrDefault(s => s != null && string.Equals(s.EnumName, axisName, StringComparison.OrdinalIgnoreCase))
                ?? servos.FirstOrDefault(s => s != null && string.Equals(s.Name, axisName, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<GantryPosition> LoadGantryPositions(string moduleName)
        {
            var definition = GetGantryModuleDefinition(moduleName);
            var servos = MachineConfiguration.ReadServos(_dataDirectory) ?? new List<ServoConfiguration>();

            var positions = new List<GantryPosition>();
            foreach (var axis in definition.Axes)
            {
                var servo = FindServo(servos, axis.Name);
                foreach (var taught in servo?.ConfiguredPositions ?? new List<AxisPosition>())
                {
                    string name = (taught?.Name ?? string.Empty).Trim();
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    var position = positions.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (position == null)
                    {
                        position = new GantryPosition { Name = name };
                        positions.Add(position);
                    }

                    if (!position.Axes.ContainsKey(axis.Name))
                    {
                        position.Axes[axis.Name] = new GantryAxisMove
                        {
                            Value = taught.Value,
                            Speed = taught.Speed,
                            Acceleration = taught.Acceleration,
                            Deceleration = taught.Deceleration,
                        };
                    }
                }
            }

            return positions;
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

        public DynamicTableDocument LoadGantryRecipeDynamic(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                throw new ArgumentException("Module name is required.", nameof(moduleName));
            }

            var definition = GetGantryModuleDefinition(moduleName);
            var rows = new List<Dictionary<string, string>>();
            foreach (var position in LoadGantryPositions(moduleName))
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [GantryRecipeColumns.Position] = position.Name,
                };

                foreach (var axis in definition.Axes)
                {
                    // A servo that does not have this position yet shows zeros; Save adds it.
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
            };
        }

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

            var definition = GetGantryModuleDefinition(moduleName);
            if (definition.Axes.Count == 0)
            {
                throw new InvalidOperationException(moduleName + " has no axes in Data\\" + MachineConfiguration.GantryFile + ".");
            }

            var servos = MachineConfiguration.ReadServos(_dataDirectory);
            if (servos == null || servos.Count == 0)
            {
                throw new InvalidOperationException("Data\\" + MachineConfiguration.ServosFile + " is missing or cannot be read.");
            }

            var oldDoc = LoadGantryRecipeDynamic(moduleName);

            // Every cell is checked before anything changes, so a bad cell saves nothing.
            var parsed = new List<KeyValuePair<string, Dictionary<string, GantryAxisMove>>>();
            foreach (var row in document.Rows)
            {
                string name = Cell(row, GantryRecipeColumns.Position);
                if (name.Length == 0)
                {
                    continue;
                }

                var moves = new Dictionary<string, GantryAxisMove>(StringComparer.OrdinalIgnoreCase);
                foreach (var axis in definition.Axes)
                {
                    moves[axis.Name] = new GantryAxisMove
                    {
                        Value = GantryNumber(name, row, GantryRecipeColumns.Value(axis.Name)),
                        Speed = GantryNumber(name, row, GantryRecipeColumns.Speed(axis.Name)),
                        Acceleration = GantryNumber(name, row, GantryRecipeColumns.Acc(axis.Name)),
                        Deceleration = GantryNumber(name, row, GantryRecipeColumns.Dec(axis.Name)),
                    };
                }

                parsed.Add(new KeyValuePair<string, Dictionary<string, GantryAxisMove>>(name, moves));
            }

            bool modified = false;
            var missingServos = new List<string>();
            foreach (var axis in definition.Axes)
            {
                var servo = FindServo(servos, axis.Name);
                if (servo == null)
                {
                    missingServos.Add(axis.Name);
                    continue;
                }

                if (servo.ConfiguredPositions == null)
                {
                    servo.ConfiguredPositions = new List<AxisPosition>();
                }

                foreach (var entry in parsed)
                {
                    var move = entry.Value[axis.Name];
                    var existing = servo.ConfiguredPositions.FirstOrDefault(p => string.Equals((p?.Name ?? string.Empty).Trim(), entry.Key, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        servo.ConfiguredPositions.Add(new AxisPosition
                        {
                            Name = entry.Key,
                            Value = move.Value,
                            Speed = move.Speed,
                            Acceleration = move.Acceleration,
                            Deceleration = move.Deceleration,
                        });
                        modified = true;
                    }
                    else if (existing.Value != move.Value || existing.Speed != move.Speed
                        || existing.Acceleration != move.Acceleration || existing.Deceleration != move.Deceleration)
                    {
                        existing.Value = move.Value;
                        existing.Speed = move.Speed;
                        existing.Acceleration = move.Acceleration;
                        existing.Deceleration = move.Deceleration;
                        modified = true;
                    }
                }
            }

            if (missingServos.Count > 0)
            {
                throw new InvalidOperationException(moduleName + " names axes that are not servos in Data\\" + MachineConfiguration.ServosFile
                    + ": " + string.Join(", ", missingServos) + ". Nothing was saved.");
            }

            if (!modified)
            {
                return RecipeSaveResult.NoChanges("No data modified !");
            }

            MachineConfiguration.SaveServos(_dataDirectory, servos);
            RecipeChanged?.Invoke(this, EventArgs.Empty);

            var columns = oldDoc.Columns;
            string[] headers = columns.Select(c => c.Title).ToArray();
            var oldRowArray = oldDoc.Rows.Select(r => columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();
            var newRowArray = document.Rows.Select(r => columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();
            AppendEditLog(MachineConfiguration.ServosFile + " (" + moduleName.Trim() + ")", headers, oldRowArray, newRowArray);

            return RecipeSaveResult.Saved("Data saved successfully !");
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
