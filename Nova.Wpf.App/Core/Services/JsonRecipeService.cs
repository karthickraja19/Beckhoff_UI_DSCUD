using System;
using System.Collections.Generic;
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
        private const string GantryStructureFile = "GantryStructure.json";
        private const string AxisParametersFile = "Axis_Parameters.xml";
        private const string SettingsFolder = "Settings";

        private const string RobotRecipeJsonFile = "RobotRecipe.json";
        private const string LightRecipeJsonFile = "LightRecipe.json";
        private const string RobotRecipeCsvFile = "RoboReceipe_Param.csv";
        private const string LightRecipeCsvFile = "LightControl_Param.csv";
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


        private class GantryPriorityConfig
        {
            public List<string> MovementPriority { get; set; }
            public List<string> SafePositionPriority { get; set; }

            /// <summary>The PLC gantry (E_Recipe) the module's recipe is written to; 0 = none.</summary>
            public int PlcGantry { get; set; }
        }

        private Dictionary<string, GantryPriorityConfig> LoadGantryStructureConfig()
        {
            string path = Path.Combine(_dataDirectory, GantryStructureFile);
            if (!File.Exists(path))
            {
                return new Dictionary<string, GantryPriorityConfig>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                string content = File.ReadAllText(path, Encoding.UTF8);
                var config = JsonConvert.DeserializeObject<Dictionary<string, GantryPriorityConfig>>(content);
                return config ?? new Dictionary<string, GantryPriorityConfig>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, GantryPriorityConfig>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public IReadOnlyList<string> GetGantryModules()
        {
            // Gantry.json is machine configuration, read through Master.Configuration.
            var gantryRoot = MachineConfiguration.LoadGantry(_dataDirectory);
            if (gantryRoot?.Gantry != null && gantryRoot.Gantry.Count > 0)
            {
                var modules = gantryRoot.Gantry
                    .Where(g => g.Name != null)
                    .Select(g => g.Name.Trim())
                    .Where(n => !string.IsNullOrWhiteSpace(n) && !n.Equals("Select", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (modules.Count > 0)
                {
                    return modules;
                }
            }

            var config = LoadGantryStructureConfig();
            return config.Keys
                .Where(name => !string.IsNullOrWhiteSpace(name) && !name.Equals("Select", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public GantryModuleDefinition GetGantryModuleDefinition(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                throw new ArgumentException("Module name is required.", nameof(moduleName));
            }

            var config = LoadGantryStructureConfig();
            if (config.TryGetValue(moduleName, out var priorities))
            {
                return new GantryModuleDefinition
                {
                    ModuleName = moduleName.Trim(),
                    MovementPriority = priorities.MovementPriority ?? new List<string>(0),
                    SafePositionPriority = priorities.SafePositionPriority ?? new List<string>(0),
                    PlcGantry = priorities.PlcGantry,
                };
            }

            return new GantryModuleDefinition
            {
                ModuleName = moduleName.Trim(),
                MovementPriority = new List<string>(0),
                SafePositionPriority = new List<string>(0)
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
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                throw new ArgumentException("Module name is required.", nameof(moduleName));
            }

            string jsonPath = GetGantryJsonRecipePath(moduleName);
            if (File.Exists(jsonPath))
            {
                try
                {
                    string jsonContent = File.ReadAllText(jsonPath, Encoding.UTF8);
                    var doc = ParseDynamicJson(jsonContent, moduleName, new[] { "Position" });
                    if (doc != null && doc.Columns.Count > 0 && doc.Rows.Count > 0)
                    {
                        return doc;
                    }
                }
                catch
                {
                    // Fallback to legacy CSV reading
                }
            }

            // Fallback to legacy CSV reading
            string csvPath = GetGantryCsvRecipePath(moduleName);
            var rows = ReadCsvRows(csvPath);
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
            var rowDicts = new List<Dictionary<string, string>>(rows.Count);
            foreach (var row in rows)
            {
                rowDicts.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Position"] = GetCell(row, 0),
                    ["X"] = GetCell(row, 1),
                    ["XVel"] = GetCell(row, 2),
                    ["XAcc"] = GetCell(row, 3),
                    ["XDeAcc"] = GetCell(row, 4),
                    ["Y"] = GetCell(row, 5),
                    ["YVel"] = GetCell(row, 6),
                    ["YAcc"] = GetCell(row, 7),
                    ["YDeAcc"] = GetCell(row, 8),
                    ["Z"] = GetCell(row, 9),
                    ["ZVel"] = GetCell(row, 10),
                    ["ZAcc"] = GetCell(row, 11),
                    ["ZDeAcc"] = GetCell(row, 12),
                });
            }

            return new DynamicTableDocument
            {
                Title = moduleName,
                Columns = columns,
                Rows = rowDicts
            };
        }

        public IReadOnlyList<GantryPositionRecord> LoadGantryRecipe(string moduleName)
        {
            var doc = LoadGantryRecipeDynamic(moduleName);
            var result = new List<GantryPositionRecord>(doc.Rows.Count);
            foreach (var r in doc.Rows)
            {
                result.Add(new GantryPositionRecord
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
                });
            }

            return result;
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

            var oldDoc = LoadGantryRecipeDynamic(moduleName);
            string jsonPath = GetGantryJsonRecipePath(moduleName);

            var jArray = new Newtonsoft.Json.Linq.JArray();
            foreach (var row in document.Rows)
            {
                var jObj = new Newtonsoft.Json.Linq.JObject();
                foreach (var col in document.Columns)
                {
                    jObj[col.Key] = row.TryGetValue(col.Key, out var val) ? val : string.Empty;
                }
                jArray.Add(jObj);
            }

            string newJson = jArray.ToString(Formatting.Indented);
            string oldJson = File.Exists(jsonPath) ? File.ReadAllText(jsonPath, Encoding.UTF8) : string.Empty;

            if (!string.IsNullOrEmpty(oldJson) && Normalize(oldJson).Equals(Normalize(newJson), StringComparison.Ordinal))
            {
                return RecipeSaveResult.NoChanges("No data modified !");
            }

            WriteTextDual(jsonPath, string.Equals(moduleName, "Gantry", StringComparison.OrdinalIgnoreCase) ? MachineConfiguration.GantryFile : moduleName + "_Gantry.json", newJson);

            // Sync with Servos.json if present
            var legacyRecords = LoadGantryRecipe(moduleName);
            SyncServosJsonIfPresent(moduleName, legacyRecords);

            string[] headers = document.Columns.Select(c => c.Title).ToArray();
            var oldRowArray = oldDoc.Rows.Select(r => document.Columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();
            var newRowArray = document.Rows.Select(r => document.Columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();

            AppendEditLog(Path.GetFileName(jsonPath), headers, oldRowArray, newRowArray);
            return RecipeSaveResult.Saved("Data saved successfully !");
        }

        public DynamicTableDocument LoadLightRecipeDynamic()
        {
            string jsonPath = Path.Combine(_dataDirectory, LightRecipeJsonFile);
            if (File.Exists(jsonPath))
            {
                try
                {
                    string jsonContent = File.ReadAllText(jsonPath, Encoding.UTF8);
                    var doc = ParseDynamicJson(jsonContent, "Light Control", new[] { "ColourCode", "Colour", "ColourName" });
                    if (doc != null && doc.Columns.Count > 0 && doc.Rows.Count > 0)
                    {
                        return doc;
                    }
                }
                catch
                {
                    // Fallback to CSV
                }
            }

            // Fallback to CSV
            string csvPath = Path.Combine(_dataDirectory, LightRecipeCsvFile);
            var rows = ReadCsvRows(csvPath);
            var columns = new List<DynamicColumnSchema>
            {
                new DynamicColumnSchema { Key = "ColourCode", Title = "Colour Code", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = "ColourName", Title = "Colour", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = "Value", Title = "Value", IsReadOnly = false },
            };
            var rowDicts = new List<Dictionary<string, string>>(rows.Count);
            foreach (var row in rows)
            {
                rowDicts.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ColourCode"] = GetCell(row, 0),
                    ["ColourName"] = GetCell(row, 1),
                    ["Value"] = GetCell(row, 2),
                });
            }

            return new DynamicTableDocument
            {
                Title = "Light Control",
                Columns = columns,
                Rows = rowDicts
            };
        }

        public IReadOnlyList<LightChannelRecord> LoadLightRecipe()
        {
            var doc = LoadLightRecipeDynamic();
            var result = new List<LightChannelRecord>(doc.Rows.Count);
            foreach (var r in doc.Rows)
            {
                result.Add(new LightChannelRecord
                {
                    ColourCode = r.TryGetValue("ColourCode", out var cc) ? cc : string.Empty,
                    Colour = r.TryGetValue("ColourName", out var cn) ? cn : (r.TryGetValue("Colour", out var c) ? c : string.Empty),
                    Value = r.TryGetValue("Value", out var v) ? v : string.Empty,
                });
            }

            return result;
        }

        public RecipeSaveResult SaveLightRecipeDynamic(DynamicTableDocument document)
        {
            if (document == null || document.Rows == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var oldDoc = LoadLightRecipeDynamic();
            string jsonPath = Path.Combine(_dataDirectory, LightRecipeJsonFile);

            var jArray = new Newtonsoft.Json.Linq.JArray();
            foreach (var row in document.Rows)
            {
                var jObj = new Newtonsoft.Json.Linq.JObject();
                foreach (var col in document.Columns)
                {
                    jObj[col.Key] = row.TryGetValue(col.Key, out var val) ? val : string.Empty;
                }
                jArray.Add(jObj);
            }

            string newJson = jArray.ToString(Formatting.Indented);
            string oldJson = File.Exists(jsonPath) ? File.ReadAllText(jsonPath, Encoding.UTF8) : string.Empty;

            if (!string.IsNullOrEmpty(oldJson) && Normalize(oldJson).Equals(Normalize(newJson), StringComparison.Ordinal))
            {
                return RecipeSaveResult.NoChanges("No data modified !");
            }

            WriteTextDual(jsonPath, LightRecipeJsonFile, newJson);

            string[] headers = document.Columns.Select(c => c.Title).ToArray();
            var oldRowArray = oldDoc.Rows.Select(r => document.Columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();
            var newRowArray = document.Rows.Select(r => document.Columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();

            AppendEditLog(Path.GetFileName(jsonPath), headers, oldRowArray, newRowArray);
            return RecipeSaveResult.Saved("Light recipe saved successfully.");
        }

        public RecipeSaveResult SaveLightRecipe(IReadOnlyList<LightChannelRecord> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            var doc = LoadLightRecipeDynamic();
            var rowDicts = new List<Dictionary<string, string>>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var dict = i < doc.Rows.Count ? new Dictionary<string, string>(doc.Rows[i], StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                dict["ColourCode"] = rows[i].ColourCode;
                dict["ColourName"] = rows[i].Colour;
                dict["Value"] = rows[i].Value;
                rowDicts.Add(dict);
            }

            var newDoc = new DynamicTableDocument
            {
                Title = doc.Title,
                Columns = doc.Columns,
                Rows = rowDicts
            };

            return SaveLightRecipeDynamic(newDoc);
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

            var jArray = new JArray();
            foreach (var row in document.Rows)
            {
                var jObj = new JObject();
                foreach (var col in document.Columns)
                {
                    jObj[col.Key] = row.TryGetValue(col.Key, out var val) ? val : string.Empty;
                }
                jArray.Add(jObj);
            }

            string newJson = jArray.ToString(Formatting.Indented);
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

            var doc = LoadGantryRecipeDynamic(moduleName);
            var rowDicts = new List<Dictionary<string, string>>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var dict = i < doc.Rows.Count ? new Dictionary<string, string>(doc.Rows[i], StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                dict["Position"] = rows[i].Position;
                dict["X"] = rows[i].X;
                dict["XVel"] = rows[i].XVel;
                dict["XAcc"] = rows[i].XAcc;
                dict["XDeAcc"] = rows[i].XDeAcc;
                dict["Y"] = rows[i].Y;
                dict["YVel"] = rows[i].YVel;
                dict["YAcc"] = rows[i].YAcc;
                dict["YDeAcc"] = rows[i].YDeAcc;
                dict["Z"] = rows[i].Z;
                dict["ZVel"] = rows[i].ZVel;
                dict["ZAcc"] = rows[i].ZAcc;
                dict["ZDeAcc"] = rows[i].ZDeAcc;
                rowDicts.Add(dict);
            }

            var newDoc = new DynamicTableDocument
            {
                Title = doc.Title,
                Columns = doc.Columns,
                Rows = rowDicts
            };

            return SaveGantryRecipeDynamic(moduleName, newDoc);
        }

        /// <summary>
        /// Copies the taught positions into Servos.json. Gantry.json and Servos.json are machine
        /// configuration, so both are read and written through Master.Configuration.
        /// </summary>
        private void SyncServosJsonIfPresent(string moduleName, IReadOnlyList<GantryPositionRecord> rows)
        {
            try
            {
                var gantryRoot = MachineConfiguration.LoadGantry(_dataDirectory);
                var module = gantryRoot?.Gantry?.FirstOrDefault(g => string.Equals(g.Name, moduleName, StringComparison.OrdinalIgnoreCase));
                if (module?.Axes == null) return;

                var servoList = MachineConfiguration.ReadServos(_dataDirectory);
                if (servoList == null) return;

                foreach (var axisRef in module.Axes)
                {
                    var servo = servoList.FirstOrDefault(s => s.Name.Equals(axisRef.Name, StringComparison.OrdinalIgnoreCase));
                    if (servo == null) continue;

                    if (servo.ConfiguredPositions == null)
                    {
                        servo.ConfiguredPositions = new List<AxisPosition>();
                    }

                    foreach (var record in rows)
                    {
                        var existing = servo.ConfiguredPositions.FirstOrDefault(p => p.Name.Equals(record.Position, StringComparison.OrdinalIgnoreCase));
                        double val = 0;
                        double acc = 0;
                        double dec = 0;
                        int speed = 0;

                        string axisType = axisRef.Type?.ToUpperInvariant() ?? string.Empty;
                        if (axisType.Contains("X"))
                        {
                            double.TryParse(record.X, out val);
                            int.TryParse(record.XVel, out speed);
                            double.TryParse(record.XAcc, out acc);
                            double.TryParse(record.XDeAcc, out dec);
                        }
                        else if (axisType.Contains("Y"))
                        {
                            double.TryParse(record.Y, out val);
                            int.TryParse(record.YVel, out speed);
                            double.TryParse(record.YAcc, out acc);
                            double.TryParse(record.YDeAcc, out dec);
                        }
                        else if (axisType.Contains("Z"))
                        {
                            double.TryParse(record.Z, out val);
                            int.TryParse(record.ZVel, out speed);
                            double.TryParse(record.ZAcc, out acc);
                            double.TryParse(record.ZDeAcc, out dec);
                        }

                        if (existing != null)
                        {
                            existing.Value = val;
                            existing.Speed = speed;
                            existing.Acceleration = acc;
                            existing.Deceleration = dec;
                        }
                        else
                        {
                            servo.ConfiguredPositions.Add(new AxisPosition
                            {
                                Name = record.Position,
                                Value = val,
                                Speed = speed,
                                Acceleration = acc,
                                Deceleration = dec
                            });
                        }
                    }
                }

                MachineConfiguration.SaveServos(_dataDirectory, servoList);
            }
            catch
            {
                // Fail-safe
            }
        }

        private string GetGantryJsonRecipePath(string moduleName)
        {
            string fileName = "Gantry_" + moduleName.Trim() + ".json";
            return Path.Combine(_dataDirectory, fileName);
        }

        private string GetGantryCsvRecipePath(string moduleName)
        {
            string cleanModule = moduleName.Trim();

            string file1 = Path.Combine(_dataDirectory, cleanModule + "Receipe_Param.csv");
            if (File.Exists(file1)) return file1;

            string file2 = Path.Combine(_dataDirectory, "Receipe_Param_" + cleanModule + ".csv");
            if (File.Exists(file2)) return file2;

            string file3 = Path.Combine(_dataDirectory, cleanModule + "_Receipe_Param.csv");
            if (File.Exists(file3)) return file3;

            string file4 = Path.Combine(_dataDirectory, "Receipe_Param.csv");
            if (File.Exists(file4)) return file4;

            return file1;
        }


        private static string ReadAxisName(XElement axisNode)
        {
            string nodeName = axisNode.Name.LocalName.Trim();
            if (nodeName.Equals("AxisName", StringComparison.OrdinalIgnoreCase))
            {
                return axisNode.Value.Trim();
            }

            var axisNameElement = axisNode.Element("AxisName");
            if (axisNameElement != null && !string.IsNullOrWhiteSpace(axisNameElement.Value))
            {
                return axisNameElement.Value.Trim();
            }

            return nodeName;
        }

        private static bool TryReadHomingSpeed(XElement axisNode, out double speed)
        {
            speed = 0;
            var element = axisNode.Element("HomingSpeed");
            if (element == null || string.IsNullOrWhiteSpace(element.Value))
            {
                return false;
            }

            return double.TryParse(element.Value.Trim(), out speed);
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
