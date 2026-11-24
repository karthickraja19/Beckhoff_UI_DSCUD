using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json;
using Master.Configuration;
using Master.Configuration.Model;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    public sealed class JsonMachineSettingsService : IMachineSettingsService
    {
        private const string SettingsFolder = "Settings";
        private const string TcpSettingsJsonFile = "TcpSettings.json";
        private const string SerialSettingsJsonFile = "SerialSettings.json";
        private const string VisionSettingsJsonFile = "VisionSettings.json";
        private const string AeProductionJsonFile = "AeLimits_Production.json";
        private const string AeCpkJsonFile = "AeLimits_CPK.json";
        private const string AeGrrJsonFile = "AeLimits_GRR.json";

        private const string TcpSettingsXmlFile = "TCPIP_Settings.xml";
        private const string SerialSettingsXmlFile = "SerialComm_Settings.xml";
        private const string VisionSettingsXmlFile = "Vision_Settings.xml";
        private const string AeProductionXmlFile = "AE_LIMITS_PRODUCTION.xml";
        private const string AeCpkXmlFile = "AE_LIMITS_CPK.xml";
        private const string AeGrrXmlFile = "AE_LIMITS_GRR.xml";

        private const string EditLogFile = "Recipe_Edit_Log.csv";

        private readonly string _dataDirectory;
        private readonly IAppSession _appSession;

        public event EventHandler SettingsChanged;

        public JsonMachineSettingsService(string dataDirectory, IAppSession appSession)
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
                string sourceDir = FindSourceProjectSettingsDirectory();
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

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private static string FindSourceProjectSettingsDirectory()
        {
            try
            {
                string current = AppDomain.CurrentDomain.BaseDirectory;
                DirectoryInfo dir = new DirectoryInfo(current);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "Nova.Wpf.App", "Data", "Settings");
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
        // Axis Configuration: Data\Servos.json
        //
        // The table is the servos of Servos.json, the same entries the Recipe page's Gantry
        // tab teaches positions into. Nothing is kept apart from it: a load reads the file as
        // it is on disk now, and a save changes only the parameter fields of the servos shown,
        // leaving every other field - above all ConfiguredPositions - as the file has it. So a
        // position saved on the Recipe page and a parameter saved here never overwrite each other.
        // ==================================================================

        private const string AxisKeyName = "EnumName";
        private const string AxisKeyId = "Id";
        private const string AxisKeyType = "Type";
        private const string AxisKeyPulse = "Pulse";
        private const string AxisKeyVelocity = "Velocity";
        private const string AxisKeyAcceleration = "Acceleration";
        private const string AxisKeyDeceleration = "Deceleration";
        private const string AxisKeyPositiveLimit = "PositiveLimit";
        private const string AxisKeyNegativeLimit = "NegativeLimit";
        private const string AxisKeyTimeout = "Timeout";

        private static string FormatNumber(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

        /// <summary>The Axis Configuration table's columns: the identity of the servo, then the parameters Servos.json holds for it.</summary>
        private static List<DynamicColumnSchema> ServoColumns()
        {
            return new List<DynamicColumnSchema>
            {
                new DynamicColumnSchema { Key = AxisKeyName, Title = "AXIS NAME", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = AxisKeyId, Title = "AXIS ID", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = AxisKeyType, Title = "TYPE", IsReadOnly = true },
                new DynamicColumnSchema { Key = AxisKeyPulse, Title = "PULSE", DataType = ColumnDataType.NonNegative },
                new DynamicColumnSchema { Key = AxisKeyVelocity, Title = "VELOCITY", DataType = ColumnDataType.NonNegative },
                new DynamicColumnSchema { Key = AxisKeyAcceleration, Title = "ACCELERATION", DataType = ColumnDataType.NonNegative },
                new DynamicColumnSchema { Key = AxisKeyDeceleration, Title = "DECELERATION", DataType = ColumnDataType.NonNegative },
                new DynamicColumnSchema { Key = AxisKeyPositiveLimit, Title = "POSITIVE LIMIT", DataType = ColumnDataType.Numeric },
                new DynamicColumnSchema { Key = AxisKeyNegativeLimit, Title = "NEGATIVE LIMIT", DataType = ColumnDataType.Numeric },
                new DynamicColumnSchema { Key = AxisKeyTimeout, Title = "TIMEOUT (MS)", DataType = ColumnDataType.NonNegative },
            };
        }

        private static Dictionary<string, string> ServoRow(ServoConfiguration servo)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AxisKeyName] = servo.EnumName ?? string.Empty,
                [AxisKeyId] = servo.Id.ToString(CultureInfo.InvariantCulture),
                [AxisKeyType] = servo.Type.ToString(),
                [AxisKeyPulse] = FormatNumber(servo.Pulse),
                [AxisKeyVelocity] = FormatNumber(servo.Velocity),
                [AxisKeyAcceleration] = FormatNumber(servo.Acceleration),
                [AxisKeyDeceleration] = FormatNumber(servo.Deceleration),
                [AxisKeyPositiveLimit] = FormatNumber(servo.PositiveLimit),
                [AxisKeyNegativeLimit] = FormatNumber(servo.NegativeLimit),
                [AxisKeyTimeout] = servo.Timeout.ToString(CultureInfo.InvariantCulture),
            };
        }

        public DynamicTableDocument LoadAxisParametersDynamic()
        {
            var servos = MachineConfiguration.ReadServos(_dataDirectory) ?? new List<ServoConfiguration>();

            return new DynamicTableDocument
            {
                Title = "Axis Configuration",
                Columns = ServoColumns(),
                Rows = servos
                    .Where(s => s != null && !string.IsNullOrWhiteSpace(s.EnumName))
                    .Select(ServoRow)
                    .ToList(),
            };
        }

        public IReadOnlyList<AxisParameterRecord> LoadAxisParameters()
        {
            var servos = MachineConfiguration.ReadServos(_dataDirectory) ?? new List<ServoConfiguration>();
            return servos
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.EnumName))
                .Select(s => new AxisParameterRecord
                {
                    AxisName = s.EnumName,
                    AxisId = s.Id.ToString(CultureInfo.InvariantCulture),
                    Type = s.Type.ToString(),
                    Pulse = FormatNumber(s.Pulse),
                    Velocity = FormatNumber(s.Velocity),
                    Acceleration = FormatNumber(s.Acceleration),
                    Deceleration = FormatNumber(s.Deceleration),
                    PositiveLimit = FormatNumber(s.PositiveLimit),
                    NegativeLimit = FormatNumber(s.NegativeLimit),
                    Timeout = s.Timeout.ToString(CultureInfo.InvariantCulture),
                })
                .ToList();
        }

        public RecipeSaveResult SaveAxisParametersDynamic(DynamicTableDocument document)
        {
            if (document == null || document.Rows == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var servos = MachineConfiguration.ReadServos(_dataDirectory);
            if (servos == null || servos.Count == 0)
            {
                throw new InvalidOperationException("Data\\" + MachineConfiguration.ServosFile + " is missing or cannot be read.");
            }

            var oldDoc = LoadAxisParametersDynamic();

            // Every cell is checked before anything changes, so a bad cell saves nothing.
            var edits = new List<KeyValuePair<ServoConfiguration, Dictionary<string, double>>>();
            var unknown = new List<string>();
            foreach (var row in document.Rows)
            {
                string name = AxisCell(row, AxisKeyName);
                if (name.Length == 0)
                {
                    continue;
                }

                var servo = servos.FirstOrDefault(s => s != null && string.Equals((s.EnumName ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase));
                if (servo == null)
                {
                    unknown.Add(name);
                    continue;
                }

                edits.Add(new KeyValuePair<ServoConfiguration, Dictionary<string, double>>(servo, new Dictionary<string, double>
                {
                    [AxisKeyPulse] = AxisNumber(name, row, AxisKeyPulse),
                    [AxisKeyVelocity] = AxisNumber(name, row, AxisKeyVelocity),
                    [AxisKeyAcceleration] = AxisNumber(name, row, AxisKeyAcceleration),
                    [AxisKeyDeceleration] = AxisNumber(name, row, AxisKeyDeceleration),
                    [AxisKeyPositiveLimit] = AxisNumber(name, row, AxisKeyPositiveLimit),
                    [AxisKeyNegativeLimit] = AxisNumber(name, row, AxisKeyNegativeLimit),
                    [AxisKeyTimeout] = AxisNumber(name, row, AxisKeyTimeout),
                }));
            }

            if (unknown.Count > 0)
            {
                throw new InvalidOperationException("Not a servo in Data\\" + MachineConfiguration.ServosFile + ": " + string.Join(", ", unknown) + ". Nothing was saved.");
            }

            bool modified = false;
            foreach (var edit in edits)
            {
                var servo = edit.Key;
                var v = edit.Value;
                int timeout = (int)v[AxisKeyTimeout];

                if (servo.Pulse != v[AxisKeyPulse] || servo.Velocity != v[AxisKeyVelocity]
                    || servo.Acceleration != v[AxisKeyAcceleration] || servo.Deceleration != v[AxisKeyDeceleration]
                    || servo.PositiveLimit != v[AxisKeyPositiveLimit] || servo.NegativeLimit != v[AxisKeyNegativeLimit]
                    || servo.Timeout != timeout)
                {
                    servo.Pulse = v[AxisKeyPulse];
                    servo.Velocity = v[AxisKeyVelocity];
                    servo.Acceleration = v[AxisKeyAcceleration];
                    servo.Deceleration = v[AxisKeyDeceleration];
                    servo.PositiveLimit = v[AxisKeyPositiveLimit];
                    servo.NegativeLimit = v[AxisKeyNegativeLimit];
                    servo.Timeout = timeout;
                    modified = true;
                }
            }

            if (!modified)
            {
                return RecipeSaveResult.NoChanges("No data modified !");
            }

            // ConfiguredPositions and any other field of an entry ride along untouched.
            MachineConfiguration.SaveServos(_dataDirectory, servos);
            SettingsChanged?.Invoke(this, EventArgs.Empty);

            var columns = oldDoc.Columns;
            string[] headers = columns.Select(c => c.Title).ToArray();
            var oldRowArray = oldDoc.Rows.Select(r => columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();
            var newRowArray = document.Rows.Select(r => columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();
            AppendEditLog(MachineConfiguration.ServosFile, headers, oldRowArray, newRowArray);

            return RecipeSaveResult.Saved("Axis configuration saved successfully.");
        }

        private static string AxisCell(Dictionary<string, string> row, string key)
        {
            return row != null && row.TryGetValue(key, out var value) && value != null ? value.Trim() : string.Empty;
        }

        /// <summary>The cell as a number; a blank cell is 0. Throws for anything else that is not a number.</summary>
        private static double AxisNumber(string axis, Dictionary<string, string> row, string key)
        {
            string raw = AxisCell(row, key);
            if (raw.Length == 0)
            {
                return 0d;
            }

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && !double.IsNaN(value) && !double.IsInfinity(value))
            {
                return value;
            }

            throw new InvalidOperationException(axis + ": '" + raw + "' is not a number (" + key + "). Nothing was saved.");
        }


        private static bool MatchesCategory(string name, string category)
        {
            if (string.IsNullOrEmpty(name)) return false;
            name = name.ToLowerInvariant();
            switch (category)
            {
                case "CCD": return name.StartsWith("ccd");
                case "Robot": return name.StartsWith("robot");
                case "Scanner": return name.Contains("scanner");
                case "SFC_PDCA": return name.Contains("sfc") || name.Contains("pdca");
                default: return false;
            }
        }

        public DynamicTableDocument LoadTcpConnectionsDynamic(string category)
        {
            string jsonPath = GetSettingsPath($"TcpSettings_{category}.json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    string content = File.ReadAllText(jsonPath, Encoding.UTF8);
                    var doc = ParseDynamicJson(content, $"{category} TCP Settings", new[] { "Name" });
                    if (doc != null && doc.Columns.Count > 0 && doc.Rows.Count > 0)
                    {
                        return doc;
                    }
                }
                catch
                {
                    // Fallback to XML
                }
            }

            // Fallback to XML
            string xmlPath = GetSettingsPath(TcpSettingsXmlFile);
            if (File.Exists(xmlPath))
            {
                var root = XDocument.Load(xmlPath).Root;
                if (root != null)
                {
                    var columns = new List<DynamicColumnSchema>
                    {
                        new DynamicColumnSchema { Key = "Name", Title = "Name", IsReadOnly = true, IsIdentifier = true },
                        new DynamicColumnSchema { Key = "IpAddress", Title = "IP Address", IsReadOnly = false, DataType = ColumnDataType.IpAddress },
                        new DynamicColumnSchema { Key = "Port", Title = "Port", IsReadOnly = false, DataType = ColumnDataType.Integer },
                    };
                    var rows = new List<Dictionary<string, string>>();
                    foreach (var node in root.Elements("Server"))
                    {
                        var name = ReadAttribute(node, "Name");
                        if (!MatchesCategory(name, category)) continue;
                        rows.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Name"] = name,
                            ["IpAddress"] = ReadAttribute(node, "IP"),
                            ["Port"] = ReadAttribute(node, "Port"),
                        });
                    }
                    return new DynamicTableDocument
                    {
                        Title = $"{category} TCP Settings",
                        Columns = columns,
                        Rows = rows
                    };
                }
            }

            // Fallback to legacy unified JSON if specific one doesn't exist
            string unifiedJsonPath = GetSettingsPath(TcpSettingsJsonFile);
            if (File.Exists(unifiedJsonPath))
            {
                try
                {
                    string content = File.ReadAllText(unifiedJsonPath, Encoding.UTF8);
                    var doc = ParseDynamicJson(content, $"{category} TCP Settings", new[] { "Name" });
                    doc.Rows = doc.Rows.Where(r => r.TryGetValue("Name", out var n) && MatchesCategory(n, category)).ToList();
                    return doc;
                }
                catch { }
            }

            return new DynamicTableDocument { Title = $"{category} TCP Settings" };
        }

        public IReadOnlyList<TcpConnectionRecord> LoadTcpConnections()
        {
            var categories = new[] { "CCD", "Robot", "Scanner", "SFC_PDCA" };
            var list = new List<TcpConnectionRecord>();
            foreach (var cat in categories)
            {
                var doc = LoadTcpConnectionsDynamic(cat);
                foreach (var r in doc.Rows)
                {
                    list.Add(new TcpConnectionRecord
                    {
                        Name = r.TryGetValue("Name", out var n) ? n : string.Empty,
                        IpAddress = r.TryGetValue("IpAddress", out var ip) ? ip : (r.TryGetValue("Address", out var addr) ? addr : string.Empty),
                        Port = r.TryGetValue("Port", out var p) ? p : string.Empty,
                    });
                }
            }
            return list;
        }

        public RecipeSaveResult SaveTcpConnectionsDynamic(string category, DynamicTableDocument document)
        {
            if (document == null || document.Rows == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var oldDoc = LoadTcpConnectionsDynamic(category);
            string jsonFile = $"TcpSettings_{category}.json";
            string jsonPath = GetSettingsPath(jsonFile);

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

            EnsureSettingsDirectoryExists();
            WriteTextDual(jsonPath, jsonFile, newJson);

            string[] headers = document.Columns.Select(c => c.Title).ToArray();
            var oldRowArray = oldDoc.Rows.Select(r => document.Columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();
            var newRowArray = document.Rows.Select(r => document.Columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();

            AppendEditLog(jsonFile, headers, oldRowArray, newRowArray);
            return RecipeSaveResult.Saved($"{category} TCP connections saved successfully.");
        }

        public void SaveTcpConnections(string category, IReadOnlyList<TcpConnectionRecord> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            var doc = LoadTcpConnectionsDynamic(category);
            var rowDicts = new List<Dictionary<string, string>>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var dict = i < doc.Rows.Count ? new Dictionary<string, string>(doc.Rows[i], StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                dict["Name"] = rows[i].Name;
                dict["IpAddress"] = rows[i].IpAddress;
                dict["Port"] = rows[i].Port;
                rowDicts.Add(dict);
            }

            var newDoc = new DynamicTableDocument
            {
                Title = doc.Title,
                Columns = doc.Columns,
                Rows = rowDicts
            };

            SaveTcpConnectionsDynamic(category, newDoc);
        }

        public DynamicTableDocument LoadSerialPortsDynamic()
        {
            string jsonPath = GetSettingsPath(SerialSettingsJsonFile);
            if (File.Exists(jsonPath))
            {
                try
                {
                    string content = File.ReadAllText(jsonPath, Encoding.UTF8);
                    var doc = ParseDynamicJson(content, "Serial Comm Settings", new[] { "Name" });
                    if (doc != null && doc.Columns.Count > 0 && doc.Rows.Count > 0)
                    {
                        return doc;
                    }
                }
                catch
                {
                    // Fallback to XML
                }
            }

            // Fallback to XML
            string xmlPath = GetSettingsPath(SerialSettingsXmlFile);
            if (File.Exists(xmlPath))
            {
                var root = XDocument.Load(xmlPath).Root;
                if (root != null)
                {
                    var columns = new List<DynamicColumnSchema>
                    {
                        new DynamicColumnSchema { Key = "Name", Title = "COM Port", IsReadOnly = true, IsIdentifier = true },
                        new DynamicColumnSchema { Key = "BaudRate", Title = "Baud Rate", IsReadOnly = false },
                        new DynamicColumnSchema { Key = "DataBits", Title = "Data Bits", IsReadOnly = false },
                        new DynamicColumnSchema { Key = "Parity", Title = "Parity", IsReadOnly = false },
                        new DynamicColumnSchema { Key = "StopBits", Title = "Stop Bits", IsReadOnly = false },
                        new DynamicColumnSchema { Key = "Handshake", Title = "Handshake", IsReadOnly = false },
                    };
                    var rows = new List<Dictionary<string, string>>();
                    foreach (var node in root.Elements("COM"))
                    {
                        rows.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Name"] = ReadAttribute(node, "Name"),
                            ["BaudRate"] = ReadAttribute(node, "BaudRate"),
                            ["DataBits"] = ReadAttribute(node, "DataBits"),
                            ["Parity"] = ReadAttribute(node, "Parity"),
                            ["StopBits"] = ReadAttribute(node, "StopBits"),
                            ["Handshake"] = ReadAttribute(node, "HandShake"),
                        });
                    }
                    return new DynamicTableDocument
                    {
                        Title = "Serial Comm Settings",
                        Columns = columns,
                        Rows = rows
                    };
                }
            }

            return new DynamicTableDocument { Title = "Serial Comm Settings" };
        }

        public IReadOnlyList<SerialPortRecord> LoadSerialPorts()
        {
            var doc = LoadSerialPortsDynamic();
            var list = new List<SerialPortRecord>(doc.Rows.Count);
            foreach (var r in doc.Rows)
            {
                list.Add(new SerialPortRecord
                {
                    Name = r.TryGetValue("Name", out var n) ? n : string.Empty,
                    BaudRate = r.TryGetValue("BaudRate", out var b) ? b : (r.TryGetValue("Address", out var addr) ? addr : string.Empty),
                    DataBits = r.TryGetValue("DataBits", out var db) ? db : (r.TryGetValue("Port", out var p) ? p : string.Empty),
                    Parity = r.TryGetValue("Parity", out var par) ? par : string.Empty,
                    StopBits = r.TryGetValue("StopBits", out var sb) ? sb : string.Empty,
                    HandShake = r.TryGetValue("Handshake", out var hs) ? hs : string.Empty,
                });
            }
            return list;
        }

        public RecipeSaveResult SaveSerialPortsDynamic(DynamicTableDocument document)
        {
            if (document == null || document.Rows == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var oldDoc = LoadSerialPortsDynamic();
            string jsonPath = GetSettingsPath(SerialSettingsJsonFile);

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

            EnsureSettingsDirectoryExists();
            WriteTextDual(jsonPath, SerialSettingsJsonFile, newJson);

            string[] headers = document.Columns.Select(c => c.Title).ToArray();
            var oldRowArray = oldDoc.Rows.Select(r => document.Columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();
            var newRowArray = document.Rows.Select(r => document.Columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();

            AppendEditLog(SerialSettingsJsonFile, headers, oldRowArray, newRowArray);
            return RecipeSaveResult.Saved("Serial ports saved successfully.");
        }

        public void SaveSerialPorts(IReadOnlyList<SerialPortRecord> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            var doc = LoadSerialPortsDynamic();
            var rowDicts = new List<Dictionary<string, string>>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var dict = i < doc.Rows.Count ? new Dictionary<string, string>(doc.Rows[i], StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                dict["Name"] = rows[i].Name;
                dict["BaudRate"] = rows[i].BaudRate;
                dict["DataBits"] = rows[i].DataBits;
                dict["Parity"] = rows[i].Parity;
                dict["StopBits"] = rows[i].StopBits;
                dict["Handshake"] = rows[i].HandShake;
                rowDicts.Add(dict);
            }

            var newDoc = new DynamicTableDocument
            {
                Title = doc.Title,
                Columns = doc.Columns,
                Rows = rowDicts
            };

            SaveSerialPortsDynamic(newDoc);
        }

        public DynamicTableDocument LoadVisionSettingsDynamic()
        {
            string jsonPath = GetSettingsPath(VisionSettingsJsonFile);
            if (File.Exists(jsonPath))
            {
                try
                {
                    string content = File.ReadAllText(jsonPath, Encoding.UTF8);
                    var doc = ParseDynamicJson(content, "Vision Settings", new[] { "Colour" });
                    if (doc != null && doc.Columns.Count > 0 && doc.Rows.Count > 0)
                    {
                        return doc;
                    }
                }
                catch
                {
                    // Fallback to XML
                }
            }

            // Fallback to XML
            string xmlPath = GetSettingsPath(VisionSettingsXmlFile);
            if (File.Exists(xmlPath))
            {
                var root = XDocument.Load(xmlPath).Root;
                if (root != null)
                {
                    var columns = new List<DynamicColumnSchema>
                    {
                        new DynamicColumnSchema { Key = "Colour", Title = "Colour", IsReadOnly = true, IsIdentifier = true },
                        new DynamicColumnSchema { Key = "OptId", Title = "OPT ID", IsReadOnly = false },
                        new DynamicColumnSchema { Key = "CameraId", Title = "Camera ID", IsReadOnly = false },
                    };
                    var rows = new List<Dictionary<string, string>>();
                    foreach (var node in root.Elements("Table_Vision_Settings"))
                    {
                        rows.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Colour"] = ReadElement(node, "Colour", "Color"),
                            ["OptId"] = ReadElement(node, "OPT_x0020_ID", "OPT_ID", "OptId"),
                            ["CameraId"] = ReadElement(node, "Camera_x0020_ID", "Camera_ID", "CameraId"),
                        });
                    }
                    return new DynamicTableDocument
                    {
                        Title = "Vision Settings",
                        Columns = columns,
                        Rows = rows
                    };
                }
            }

            return new DynamicTableDocument { Title = "Vision Settings" };
        }

        public IReadOnlyList<VisionParameterRecord> LoadVisionSettings()
        {
            var doc = LoadVisionSettingsDynamic();
            var list = new List<VisionParameterRecord>(doc.Rows.Count);
            foreach (var r in doc.Rows)
            {
                list.Add(new VisionParameterRecord
                {
                    Colour = r.TryGetValue("Colour", out var c) ? c : string.Empty,
                    OptId = r.TryGetValue("OptId", out var opt) ? opt : string.Empty,
                    CameraId = r.TryGetValue("CameraId", out var cam) ? cam : string.Empty,
                });
            }
            return list;
        }

        public RecipeSaveResult SaveVisionSettingsDynamic(DynamicTableDocument document)
        {
            if (document == null || document.Rows == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var oldDoc = LoadVisionSettingsDynamic();
            string jsonPath = GetSettingsPath(VisionSettingsJsonFile);

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

            EnsureSettingsDirectoryExists();
            WriteTextDual(jsonPath, VisionSettingsJsonFile, newJson);

            string[] headers = document.Columns.Select(c => c.Title).ToArray();
            var oldRowArray = oldDoc.Rows.Select(r => document.Columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();
            var newRowArray = document.Rows.Select(r => document.Columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();

            AppendEditLog(VisionSettingsJsonFile, headers, oldRowArray, newRowArray);
            return RecipeSaveResult.Saved("Vision settings saved successfully.");
        }

        public void SaveVisionSettings(IReadOnlyList<VisionParameterRecord> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            var doc = LoadVisionSettingsDynamic();
            var rowDicts = new List<Dictionary<string, string>>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var dict = i < doc.Rows.Count ? new Dictionary<string, string>(doc.Rows[i], StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                dict["Colour"] = rows[i].Colour;
                dict["OptId"] = rows[i].OptId;
                dict["CameraId"] = rows[i].CameraId;
                rowDicts.Add(dict);
            }

            var newDoc = new DynamicTableDocument
            {
                Title = doc.Title,
                Columns = doc.Columns,
                Rows = rowDicts
            };

            SaveVisionSettingsDynamic(newDoc);
        }

        public DynamicTableDocument LoadAeLimitsDynamic(AeLimitKind kind)
        {
            string jsonFile = GetAeJsonFileName(kind);
            string jsonPath = GetSettingsPath(jsonFile);
            if (File.Exists(jsonPath))
            {
                try
                {
                    string content = File.ReadAllText(jsonPath, Encoding.UTF8);
                    var doc = ParseDynamicJson(content, "AE Limits", new[] { "SerialNo", "Parameter" });
                    if (doc != null && doc.Columns.Count > 0 && doc.Rows.Count > 0)
                    {
                        return doc;
                    }
                }
                catch
                {
                    // Fallback to XML
                }
            }

            // Fallback to XML
            string xmlFile = GetAeXmlFileName(kind);
            string xmlPath = GetSettingsPath(xmlFile);
            if (File.Exists(xmlPath))
            {
                var root = XDocument.Load(xmlPath).Root;
                if (root != null)
                {
                    string nodeName = GetAeNodeName(kind);
                    var columns = StandardAeColumns();
                    var rows = new List<Dictionary<string, string>>();
                    int fallbackSerial = 1;
                    foreach (var node in root.Elements(nodeName))
                    {
                        string sNo = ReadElement(node, "S.No", "SNo", "SerialNo");
                        if (string.IsNullOrWhiteSpace(sNo)) sNo = (fallbackSerial++).ToString();
                        rows.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["SerialNo"] = sNo,
                            ["Parameter"] = ReadElement(node, "Parameters", "Parameter"),
                            ["Min"] = ReadElement(node, "Minimum_Limit", "Minimum_x0020_Limit"),
                            ["Max"] = ReadElement(node, "Maximum_Limit", "Maximum_x0020_Limit"),
                            ["Unit"] = ReadElement(node, "Unit"),
                        });
                    }
                    return new DynamicTableDocument
                    {
                        Title = "AE Limits",
                        Columns = columns,
                        Rows = rows
                    };
                }
            }

            return new DynamicTableDocument { Title = "AE Limits" };
        }

        public IReadOnlyList<AeLimitRecord> LoadAeLimits(AeLimitKind kind)
        {
            var doc = LoadAeLimitsDynamic(kind);
            var list = new List<AeLimitRecord>(doc.Rows.Count);
            foreach (var r in doc.Rows)
            {
                int.TryParse(r.TryGetValue("SerialNo", out var s) ? s : "0", out int sno);
                list.Add(new AeLimitRecord
                {
                    SerialNo = sno,
                    Parameter = r.TryGetValue("Parameter", out var p) ? p : string.Empty,
                    Min = r.TryGetValue("Min", out var min) ? min : string.Empty,
                    Max = r.TryGetValue("Max", out var max) ? max : string.Empty,
                    Unit = r.TryGetValue("Unit", out var u) ? u : string.Empty,
                });
            }
            return list;
        }

        public RecipeSaveResult SaveAeLimitsDynamic(AeLimitKind kind, DynamicTableDocument document)
        {
            if (document == null || document.Rows == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var oldDoc = LoadAeLimitsDynamic(kind);
            string fileName = GetAeJsonFileName(kind);
            string jsonPath = GetSettingsPath(fileName);

            // Written as Title / Columns / Rows, the shape the shipped file has. A bare
            // row array drops the column titles and the numeric type on Min and Max, and
            // every load after that warns that the schema is missing. A file that has
            // already lost its schema gets the standard AE columns back.
            IReadOnlyList<DynamicColumnSchema> columns = oldDoc.HasMissingColumnSchema || document.Columns == null || document.Columns.Count == 0
                ? WithStandardColumns(StandardAeColumns(), document.Columns)
                : document.Columns;

            var jArray = new Newtonsoft.Json.Linq.JArray();
            foreach (var row in document.Rows)
            {
                var jObj = new Newtonsoft.Json.Linq.JObject();
                foreach (var col in columns)
                {
                    jObj[col.Key] = row.TryGetValue(col.Key, out var val) ? val : string.Empty;
                }
                jArray.Add(jObj);
            }

            var root = new Newtonsoft.Json.Linq.JObject
            {
                ["Title"] = !string.IsNullOrWhiteSpace(oldDoc.Title) ? oldDoc.Title : document.Title,
                ["Columns"] = new Newtonsoft.Json.Linq.JArray(columns.Select(ColumnToJson)),
                ["Rows"] = jArray
            };

            string newJson = root.ToString(Formatting.Indented);
            string oldJson = File.Exists(jsonPath) ? File.ReadAllText(jsonPath, Encoding.UTF8) : string.Empty;

            // Compared as JSON rather than as text, so a file laid out differently by
            // hand is not reported as changed when no value moved.
            if (!string.IsNullOrEmpty(oldJson) && SameJson(oldJson, root))
            {
                return RecipeSaveResult.NoChanges("No data modified !");
            }

            EnsureSettingsDirectoryExists();
            WriteTextDual(jsonPath, GetAeJsonFileName(kind), newJson);

            string[] headers = document.Columns.Select(c => c.Title).ToArray();
            var oldRowArray = oldDoc.Rows.Select(r => document.Columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();
            var newRowArray = document.Rows.Select(r => document.Columns.Select(c => r.TryGetValue(c.Key, out var val) ? val : string.Empty).ToArray()).ToList();

            AppendEditLog(fileName, headers, oldRowArray, newRowArray);
            return RecipeSaveResult.Saved("AE limits saved successfully.");
        }

        /// <summary>
        /// The AE limit table's columns, as the shipped AeLimits_*.json files define them.
        /// </summary>
        private static List<DynamicColumnSchema> StandardAeColumns()
        {
            return new List<DynamicColumnSchema>
            {
                new DynamicColumnSchema { Key = "SerialNo", Title = "S.NO", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = "Parameter", Title = "PARAMETER", IsReadOnly = true },
                new DynamicColumnSchema { Key = "Min", Title = "MINIMUM LIMIT", DataType = ColumnDataType.Numeric },
                new DynamicColumnSchema { Key = "Max", Title = "MAXIMUM LIMIT", DataType = ColumnDataType.Numeric },
                new DynamicColumnSchema { Key = "Unit", Title = "UNIT" },
            };
        }

        /// <summary>
        /// The standard columns, plus any other column the caller had, so a file
        /// with an extra column keeps it.
        /// </summary>
        private static List<DynamicColumnSchema> WithStandardColumns(List<DynamicColumnSchema> columns, IReadOnlyList<DynamicColumnSchema> existing)
        {
            if (existing != null)
            {
                foreach (var col in existing)
                {
                    if (!columns.Any(c => c.Key.Equals(col.Key, StringComparison.OrdinalIgnoreCase)))
                        columns.Add(col);
                }
            }

            return columns;
        }

        /// <summary>
        /// One column schema entry, writing only what differs from the parser's
        /// defaults - the same shape as the hand-written files.
        /// </summary>
        private static Newtonsoft.Json.Linq.JObject ColumnToJson(DynamicColumnSchema column)
        {
            var json = new Newtonsoft.Json.Linq.JObject
            {
                ["Key"] = column.Key,
                ["Title"] = column.Title
            };
            if (column.IsReadOnly) json["IsReadOnly"] = true;
            if (column.IsIdentifier) json["IsIdentifier"] = true;
            json["DataType"] = column.DataType.ToString();
            if (!column.ValidationEnabled) json["ValidationEnabled"] = false;
            if (column.Width.HasValue) json["Width"] = column.Width.Value;
            return json;
        }

        private static bool SameJson(string existingJson, Newtonsoft.Json.Linq.JToken candidate)
        {
            try
            {
                return Newtonsoft.Json.Linq.JToken.DeepEquals(Newtonsoft.Json.Linq.JToken.Parse(existingJson), candidate);
            }
            catch (JsonException)
            {
                // An unreadable file is by definition different from what is being saved.
                return false;
            }
        }

        public void SaveAeLimits(AeLimitKind kind, IReadOnlyList<AeLimitRecord> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            var doc = LoadAeLimitsDynamic(kind);
            var rowDicts = new List<Dictionary<string, string>>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var dict = i < doc.Rows.Count ? new Dictionary<string, string>(doc.Rows[i], StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                dict["SerialNo"] = rows[i].SerialNo.ToString();
                dict["Parameter"] = rows[i].Parameter;
                dict["Min"] = rows[i].Min;
                dict["Max"] = rows[i].Max;
                dict["Unit"] = rows[i].Unit;
                rowDicts.Add(dict);
            }

            var newDoc = new DynamicTableDocument
            {
                Title = doc.Title,
                Columns = doc.Columns,
                Rows = rowDicts
            };

            SaveAeLimitsDynamic(kind, newDoc);
        }

        private static DynamicTableDocument ParseDynamicJson(string jsonContent, string defaultTitle, string[] identifierKeys = null)
        {
            var token = Newtonsoft.Json.Linq.JToken.Parse(jsonContent);
            if (token is Newtonsoft.Json.Linq.JArray jArray)
            {
                return ParseDynamicJArray(jArray, defaultTitle, identifierKeys);
            }
            else if (token is Newtonsoft.Json.Linq.JObject jObj)
            {
                string title = jObj["Title"]?.ToString() ?? defaultTitle;
                var columns = new List<DynamicColumnSchema>();
                bool hasExplicitColumns = jObj["Columns"] is Newtonsoft.Json.Linq.JArray colsArray && colsArray.Count > 0;

                if (hasExplicitColumns)
                {
                    var explicitCols = (Newtonsoft.Json.Linq.JArray)jObj["Columns"];
                    foreach (var colToken in explicitCols)
                    {
                        if (colToken is Newtonsoft.Json.Linq.JObject colObj)
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
                if (jObj["Rows"] is Newtonsoft.Json.Linq.JArray rowsArray)
                {
                    foreach (var rowToken in rowsArray)
                    {
                        if (rowToken is Newtonsoft.Json.Linq.JObject rowObj)
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

                if (!hasExplicitColumns && rows.Count > 0 && jObj["Rows"] is Newtonsoft.Json.Linq.JArray fallbackRows)
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

        private static DynamicTableDocument ParseDynamicJArray(Newtonsoft.Json.Linq.JArray jArray, string defaultTitle, string[] identifierKeys = null)
        {
            var discoveredKeys = new List<string>();
            var rows = new List<Dictionary<string, string>>();

            foreach (var item in jArray)
            {
                if (item is Newtonsoft.Json.Linq.JObject rowObj)
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

        private string GetSettingsPath(string fileName)
        {
            return Path.Combine(_dataDirectory, SettingsFolder, fileName);
        }

        private void EnsureSettingsDirectoryExists()
        {
            string dir = Path.Combine(_dataDirectory, SettingsFolder);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private static string GetAeJsonFileName(AeLimitKind kind)
        {
            switch (kind)
            {
                case AeLimitKind.Cpk:
                    return AeCpkJsonFile;
                case AeLimitKind.Grr:
                    return AeGrrJsonFile;
                default:
                    return AeProductionJsonFile;
            }
        }

        private static string GetAeXmlFileName(AeLimitKind kind)
        {
            switch (kind)
            {
                case AeLimitKind.Cpk:
                    return AeCpkXmlFile;
                case AeLimitKind.Grr:
                    return AeGrrXmlFile;
                default:
                    return AeProductionXmlFile;
            }
        }

        private static string GetAeNodeName(AeLimitKind kind)
        {
            switch (kind)
            {
                case AeLimitKind.Cpk:
                    return "AE_Limits_CPK";
                case AeLimitKind.Grr:
                    return "AE_Limits_GRR";
                default:
                    return "AE_Limits_Production";
            }
        }

        private static string ReadElement(XElement parent, params string[] names)
        {
            foreach (var name in names)
            {
                var el = parent.Element(name);
                if (el != null)
                {
                    return el.Value.Trim();
                }
            }

            return string.Empty;
        }

        private static string ReadAttribute(XElement element, string name)
        {
            var attr = element.Attribute(name);
            return attr != null ? attr.Value.Trim() : string.Empty;
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

        private static string GetCell(string[] row, int index)
        {
            if (row == null || index < 0 || index >= row.Length)
            {
                return string.Empty;
            }

            return row[index];
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
