using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    public sealed class XmlMachineSettingsService : IMachineSettingsService
    {
        private const string SettingsFolder = "Settings";
        private const string AxisParametersFile = "Axis_Parameters.xml";
        private const string TcpSettingsFile = "TCPIP_Settings.xml";
        private const string SerialSettingsFile = "SerialComm_Settings.xml";
        private const string VisionSettingsFile = "Vision_Settings.xml";
        private const string AeProductionFile = "AE_LIMITS_PRODUCTION.xml";
        private const string AeCpkFile = "AE_LIMITS_CPK.xml";
        private const string AeGrrFile = "AE_LIMITS_GRR.xml";
        private const string EditLogFile = "Recipe_Edit_Log.csv";

        private static readonly string[] AxisHeaders =
        {
            "Card_ID",
            "Axis_ID",
            "Axis_Name",
            "Pitch",
            "Pulses",
            "PPR",
            "Homing_Acc",
            "Homing_DeAcc",
            "Homing_Speed",
        };

        private readonly string _dataDirectory;
        private readonly IAppSession _appSession;

        public event EventHandler SettingsChanged;

        public XmlMachineSettingsService(string dataDirectory, IAppSession appSession)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                throw new ArgumentException("Data directory cannot be empty.", nameof(dataDirectory));
            }

            _dataDirectory = dataDirectory;
            _appSession = appSession ?? throw new ArgumentNullException(nameof(appSession));
        }

        public DynamicTableDocument LoadAxisParametersDynamic()
        {
            var records = LoadAxisParameters();
            var columns = new List<DynamicColumnSchema>
            {
                new DynamicColumnSchema { Key = "CardId", Title = "CARD ID", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = "AxisId", Title = "AXIS ID", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = "AxisName", Title = "AXIS NAME", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = "Pitch", Title = "PITCH", IsReadOnly = false },
                new DynamicColumnSchema { Key = "Pulses", Title = "PULSES/MM", IsReadOnly = false },
                new DynamicColumnSchema { Key = "PPR", Title = "PPR", IsReadOnly = false },
                new DynamicColumnSchema { Key = "HomingAcc", Title = "HOMING ACC", IsReadOnly = false },
                new DynamicColumnSchema { Key = "HomingDeAcc", Title = "HOMING DE-ACC", IsReadOnly = false },
                new DynamicColumnSchema { Key = "HomingSpeed", Title = "HOMING SPEED", IsReadOnly = false },
            };
            var rows = records.Select(r => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CardId"] = r.CardId,
                ["AxisId"] = r.AxisId,
                ["AxisName"] = r.AxisName,
                ["Pitch"] = r.Pitch,
                ["Pulses"] = r.Pulses,
                ["PPR"] = r.Ppr,
                ["HomingAcc"] = r.HomingAcc,
                ["HomingDeAcc"] = r.HomingDeAcc,
                ["HomingSpeed"] = r.HomingSpeed,
            }).ToList();

            return new DynamicTableDocument { Title = "Axis Parameters", Columns = columns, Rows = rows };
        }

        public RecipeSaveResult SaveAxisParametersDynamic(DynamicTableDocument document)
        {
            if (document == null || document.Rows == null) throw new ArgumentNullException(nameof(document));
            var list = new List<AxisParameterRecord>(document.Rows.Count);
            foreach (var r in document.Rows)
            {
                list.Add(new AxisParameterRecord
                {
                    CardId = r.TryGetValue("CardId", out var cid) ? cid : string.Empty,
                    AxisId = r.TryGetValue("AxisId", out var aid) ? aid : string.Empty,
                    AxisName = r.TryGetValue("AxisName", out var an) ? an : string.Empty,
                    Pitch = r.TryGetValue("Pitch", out var p) ? p : string.Empty,
                    Pulses = r.TryGetValue("Pulses", out var pul) ? pul : string.Empty,
                    Ppr = r.TryGetValue("PPR", out var ppr) ? ppr : string.Empty,
                    HomingAcc = r.TryGetValue("HomingAcc", out var ha) ? ha : string.Empty,
                    HomingDeAcc = r.TryGetValue("HomingDeAcc", out var hda) ? hda : string.Empty,
                    HomingSpeed = r.TryGetValue("HomingSpeed", out var hs) ? hs : string.Empty,
                });
            }
            return SaveAxisParameters(list);
        }

        public DynamicTableDocument LoadTcpConnectionsDynamic(string category)
        {
            var records = LoadTcpConnections().Where(r => MatchesCategory(r.Name, category)).ToList();
            var columns = new List<DynamicColumnSchema>
            {
                new DynamicColumnSchema { Key = "Name", Title = "Name", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = "IpAddress", Title = "IP Address", IsReadOnly = false },
                new DynamicColumnSchema { Key = "Port", Title = "Port", IsReadOnly = false },
            };
            var rows = records.Select(r => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = r.Name,
                ["IpAddress"] = r.IpAddress,
                ["Port"] = r.Port,
            }).ToList();
            return new DynamicTableDocument { Title = $"{category} TCP Settings", Columns = columns, Rows = rows };
        }

        public RecipeSaveResult SaveTcpConnectionsDynamic(string category, DynamicTableDocument document)
        {
            if (document == null || document.Rows == null) throw new ArgumentNullException(nameof(document));
            var list = document.Rows.Select(r => new TcpConnectionRecord
            {
                Name = r.TryGetValue("Name", out var n) ? n : string.Empty,
                IpAddress = r.TryGetValue("IpAddress", out var ip) ? ip : string.Empty,
                Port = r.TryGetValue("Port", out var p) ? p : string.Empty,
            }).ToList();
            SaveTcpConnections(category, list);
            return RecipeSaveResult.Saved("TCP connections saved successfully.");
        }

        public DynamicTableDocument LoadSerialPortsDynamic()
        {
            var records = LoadSerialPorts();
            var columns = new List<DynamicColumnSchema>
            {
                new DynamicColumnSchema { Key = "Name", Title = "COM Port", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = "BaudRate", Title = "Baud Rate", IsReadOnly = false },
                new DynamicColumnSchema { Key = "DataBits", Title = "Data Bits", IsReadOnly = false },
                new DynamicColumnSchema { Key = "Parity", Title = "Parity", IsReadOnly = false },
                new DynamicColumnSchema { Key = "StopBits", Title = "Stop Bits", IsReadOnly = false },
                new DynamicColumnSchema { Key = "Handshake", Title = "Handshake", IsReadOnly = false },
            };
            var rows = records.Select(r => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = r.Name,
                ["BaudRate"] = r.BaudRate,
                ["DataBits"] = r.DataBits,
                ["Parity"] = r.Parity,
                ["StopBits"] = r.StopBits,
                ["Handshake"] = r.HandShake,
            }).ToList();
            return new DynamicTableDocument { Title = "Serial Comm Settings", Columns = columns, Rows = rows };
        }

        public RecipeSaveResult SaveSerialPortsDynamic(DynamicTableDocument document)
        {
            if (document == null || document.Rows == null) throw new ArgumentNullException(nameof(document));
            var list = document.Rows.Select(r => new SerialPortRecord
            {
                Name = r.TryGetValue("Name", out var n) ? n : string.Empty,
                BaudRate = r.TryGetValue("BaudRate", out var b) ? b : string.Empty,
                DataBits = r.TryGetValue("DataBits", out var db) ? db : string.Empty,
                Parity = r.TryGetValue("Parity", out var par) ? par : string.Empty,
                StopBits = r.TryGetValue("StopBits", out var sb) ? sb : string.Empty,
                HandShake = r.TryGetValue("Handshake", out var hs) ? hs : string.Empty,
            }).ToList();
            SaveSerialPorts(list);
            return RecipeSaveResult.Saved("Serial ports saved successfully.");
        }

        public DynamicTableDocument LoadVisionSettingsDynamic()
        {
            var records = LoadVisionSettings();
            var columns = new List<DynamicColumnSchema>
            {
                new DynamicColumnSchema { Key = "Colour", Title = "Colour", IsReadOnly = false, IsIdentifier = false },
                new DynamicColumnSchema { Key = "OptId", Title = "OPT ID", IsReadOnly = false },
                new DynamicColumnSchema { Key = "CameraId", Title = "Camera ID", IsReadOnly = false },
            };
            var rows = records.Select(r => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Colour"] = r.Colour,
                ["OptId"] = r.OptId,
                ["CameraId"] = r.CameraId,
            }).ToList();
            return new DynamicTableDocument { Title = "Vision Settings", Columns = columns, Rows = rows };
        }

        public RecipeSaveResult SaveVisionSettingsDynamic(DynamicTableDocument document)
        {
            if (document == null || document.Rows == null) throw new ArgumentNullException(nameof(document));
            var list = document.Rows.Select(r => new VisionParameterRecord
            {
                Colour = r.TryGetValue("Colour", out var c) ? c : string.Empty,
                OptId = r.TryGetValue("OptId", out var opt) ? opt : string.Empty,
                CameraId = r.TryGetValue("CameraId", out var cam) ? cam : string.Empty,
            }).ToList();
            SaveVisionSettings(list);
            return RecipeSaveResult.Saved("Vision settings saved successfully.");
        }

        public DynamicTableDocument LoadAeLimitsDynamic(AeLimitKind kind)
        {
            var records = LoadAeLimits(kind);
            var columns = new List<DynamicColumnSchema>
            {
                new DynamicColumnSchema { Key = "SerialNo", Title = "S.NO", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = "Parameter", Title = "PARAMETER", IsReadOnly = true, IsIdentifier = true },
                new DynamicColumnSchema { Key = "Min", Title = "MINIMUM LIMIT", IsReadOnly = false },
                new DynamicColumnSchema { Key = "Max", Title = "MAXIMUM LIMIT", IsReadOnly = false },
                new DynamicColumnSchema { Key = "Unit", Title = "UNIT", IsReadOnly = false },
            };
            var rows = records.Select(r => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SerialNo"] = r.SerialNo.ToString(),
                ["Parameter"] = r.Parameter,
                ["Min"] = r.Min,
                ["Max"] = r.Max,
                ["Unit"] = r.Unit,
            }).ToList();
            return new DynamicTableDocument { Title = "AE Limits", Columns = columns, Rows = rows };
        }

        public RecipeSaveResult SaveAeLimitsDynamic(AeLimitKind kind, DynamicTableDocument document)
        {
            if (document == null || document.Rows == null) throw new ArgumentNullException(nameof(document));
            var list = document.Rows.Select(r =>
            {
                int.TryParse(r.TryGetValue("SerialNo", out var s) ? s : "0", out int sno);
                return new AeLimitRecord
                {
                    SerialNo = sno,
                    Parameter = r.TryGetValue("Parameter", out var p) ? p : string.Empty,
                    Min = r.TryGetValue("Min", out var min) ? min : string.Empty,
                    Max = r.TryGetValue("Max", out var max) ? max : string.Empty,
                    Unit = r.TryGetValue("Unit", out var u) ? u : string.Empty,
                };
            }).ToList();
            SaveAeLimits(kind, list);
            return RecipeSaveResult.Saved("AE limits saved successfully.");
        }

        public IReadOnlyList<AxisParameterRecord> LoadAxisParameters()
        {
            string path = GetSettingsPath(AxisParametersFile);
            var doc = LoadXmlDocument(path);
            return LoadAxisParameters(doc);
        }

        public RecipeSaveResult SaveAxisParameters(IReadOnlyList<AxisParameterRecord> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            string path = GetSettingsPath(AxisParametersFile);
            var oldDoc = LoadXmlDocument(path);
            var oldRows = LoadAxisParameters(oldDoc);
            var newDoc = BuildAxisDocument(rows);

            if (NormalizeXml(oldDoc).Equals(NormalizeXml(newDoc), StringComparison.Ordinal))
            {
                return RecipeSaveResult.NoChanges("No data modified !");
            }

            SaveXml(path, newDoc);
            AppendAxisEditLog(oldRows, rows);
            return RecipeSaveResult.Saved("Axis parameters saved successfully.");
        }

        public IReadOnlyList<TcpConnectionRecord> LoadTcpConnections()
        {
            string path = GetSettingsPath(TcpSettingsFile);
            var root = LoadXmlRoot(path);
            var result = new List<TcpConnectionRecord>();
            foreach (var node in root.Elements("Server"))
            {
                result.Add(new TcpConnectionRecord
                {
                    Name = ReadAttribute(node, "Name"),
                    IpAddress = ReadAttribute(node, "IP"),
                    Port = ReadAttribute(node, "Port"),
                });
            }

            return result;
        }

        public void SaveTcpConnections(string category, IReadOnlyList<TcpConnectionRecord> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            var existing = LoadTcpConnections().Where(r => !MatchesCategory(r.Name, category)).ToList();
            var all = existing.Concat(rows).ToList();

            string path = GetSettingsPath(TcpSettingsFile);
            EnsureExistingFile(path);
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("TCP_IP_Details",
                    all.Select(r => new XElement("Server",
                        new XAttribute("Name", r.Name ?? string.Empty),
                        new XAttribute("IP", r.IpAddress ?? string.Empty),
                        new XAttribute("Port", r.Port ?? string.Empty)))));

            SaveXml(path, doc);
        }

        public IReadOnlyList<SerialPortRecord> LoadSerialPorts()
        {
            string path = GetSettingsPath(SerialSettingsFile);
            var root = LoadXmlRoot(path);
            var result = new List<SerialPortRecord>();
            foreach (var node in root.Elements("COM"))
            {
                result.Add(new SerialPortRecord
                {
                    Name = ReadAttribute(node, "Name"),
                    BaudRate = ReadAttribute(node, "BaudRate"),
                    DataBits = ReadAttribute(node, "DataBits"),
                    Parity = ReadAttribute(node, "Parity"),
                    StopBits = ReadAttribute(node, "StopBits"),
                    HandShake = ReadAttribute(node, "HandShake"),
                });
            }

            return result;
        }

        public void SaveSerialPorts(IReadOnlyList<SerialPortRecord> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            string path = GetSettingsPath(SerialSettingsFile);
            EnsureExistingFile(path);
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("OPT",
                    rows.Select(r => new XElement("COM",
                        new XAttribute("Name", r.Name ?? string.Empty),
                        new XAttribute("BaudRate", r.BaudRate ?? string.Empty),
                        new XAttribute("DataBits", r.DataBits ?? string.Empty),
                        new XAttribute("Parity", r.Parity ?? string.Empty),
                        new XAttribute("StopBits", r.StopBits ?? string.Empty),
                        new XAttribute("HandShake", r.HandShake ?? string.Empty)))));

            SaveXml(path, doc);
        }

        public IReadOnlyList<VisionParameterRecord> LoadVisionSettings()
        {
            string path = GetSettingsPath(VisionSettingsFile);
            var root = LoadXmlRoot(path);
            var result = new List<VisionParameterRecord>();
            foreach (var node in root.Elements("Vision_Settings"))
            {
                result.Add(new VisionParameterRecord
                {
                    Colour = ReadElement(node, "Colour"),
                    OptId = ReadElement(node, "OPT_ID"),
                    CameraId = ReadElement(node, "Camera_ID"),
                });
            }

            return result;
        }

        public void SaveVisionSettings(IReadOnlyList<VisionParameterRecord> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            string path = GetSettingsPath(VisionSettingsFile);
            EnsureExistingFile(path);
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("DocumentElement",
                    rows.Select(r => new XElement("Vision_Settings",
                        new XElement("Colour", r.Colour ?? string.Empty),
                        new XElement("OPT_ID", r.OptId ?? string.Empty),
                        new XElement("Camera_ID", r.CameraId ?? string.Empty)))));

            SaveXml(path, doc);
        }

        public IReadOnlyList<AeLimitRecord> LoadAeLimits(AeLimitKind kind)
        {
            string path = GetSettingsPath(GetAeFileName(kind));
            var root = LoadXmlRoot(path);
            string nodeName = GetAeNodeName(kind);
            var result = new List<AeLimitRecord>();
            int fallbackSerialNo = 1;
            foreach (var node in root.Elements(nodeName))
            {
                string serialText = ReadElement(node, "S.No");
                if (!int.TryParse(serialText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int serialNo))
                {
                    serialNo = fallbackSerialNo;
                }

                result.Add(new AeLimitRecord
                {
                    SerialNo = serialNo,
                    Parameter = ReadElement(node, "Parameters"),
                    Min = ReadElement(node, "Minimum_x0020_Limit"),
                    Max = ReadElement(node, "Maximum_x0020_Limit"),
                    Unit = ReadElement(node, "Unit"),
                });
                fallbackSerialNo++;
            }

            return result;
        }

        public void SaveAeLimits(AeLimitKind kind, IReadOnlyList<AeLimitRecord> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            string path = GetSettingsPath(GetAeFileName(kind));
            EnsureExistingFile(path);
            string nodeName = GetAeNodeName(kind);
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("DocumentElement",
                    rows.Select(r => new XElement(nodeName,
                        new XElement("S.No", r.SerialNo.ToString(CultureInfo.InvariantCulture)),
                        new XElement("Parameters", r.Parameter ?? string.Empty),
                        new XElement("Minimum_x0020_Limit", r.Min ?? string.Empty),
                        new XElement("Maximum_x0020_Limit", r.Max ?? string.Empty),
                        new XElement("Unit", r.Unit ?? string.Empty)))));

            SaveXml(path, doc);
        }

        private IReadOnlyList<AxisParameterRecord> LoadAxisParameters(XDocument doc)
        {
            var root = GetRequiredRoot(doc);
            var result = new List<AxisParameterRecord>();
            foreach (var node in root.Elements("Axis_Parameters"))
            {
                result.Add(new AxisParameterRecord
                {
                    CardId = ReadElement(node, "Card_ID"),
                    AxisId = ReadElement(node, "Axis_ID"),
                    AxisName = ReadElement(node, "Axis_Name"),
                    Pitch = ReadElement(node, "Pitch"),
                    Pulses = ReadElement(node, "Pulses"),
                    Ppr = ReadElement(node, "PPR"),
                    HomingAcc = ReadElement(node, "Homing_Acc"),
                    HomingDeAcc = ReadElement(node, "Homing_DeAcc"),
                    HomingSpeed = ReadElement(node, "Homing_Speed"),
                });
            }

            return result;
        }

        private static XDocument BuildAxisDocument(IReadOnlyList<AxisParameterRecord> rows)
        {
            return new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("DocumentElement",
                    rows.Select(r => new XElement("Axis_Parameters",
                        new XElement("Card_ID", r.CardId ?? string.Empty),
                        new XElement("Axis_ID", r.AxisId ?? string.Empty),
                        new XElement("Axis_Name", r.AxisName ?? string.Empty),
                        new XElement("Pitch", r.Pitch ?? string.Empty),
                        new XElement("Pulses", r.Pulses ?? string.Empty),
                        new XElement("PPR", r.Ppr ?? string.Empty),
                        new XElement("Homing_Acc", r.HomingAcc ?? string.Empty),
                        new XElement("Homing_DeAcc", r.HomingDeAcc ?? string.Empty),
                        new XElement("Homing_Speed", r.HomingSpeed ?? string.Empty)))));
        }

        private void AppendAxisEditLog(IReadOnlyList<AxisParameterRecord> oldRows, IReadOnlyList<AxisParameterRecord> newRows)
        {
            var changes = CollectAxisChanges(oldRows, newRows);
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
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

                foreach (var change in changes)
                {
                    writer.WriteLine(string.Join(",",
                        EscapeCsv(timestamp),
                        EscapeCsv(user),
                        EscapeCsv(mode),
                        EscapeCsv(AxisParametersFile),
                        EscapeCsv(change.RowIndex.ToString(CultureInfo.InvariantCulture)),
                        EscapeCsv(change.ColumnName),
                        EscapeCsv(change.OldValue),
                        EscapeCsv(change.NewValue)));
                }
            }
        }

        private static List<AxisChange> CollectAxisChanges(IReadOnlyList<AxisParameterRecord> oldRows, IReadOnlyList<AxisParameterRecord> newRows)
        {
            var changes = new List<AxisChange>();
            int maxRows = Math.Max(oldRows.Count, newRows.Count);
            for (int rowIndex = 0; rowIndex < maxRows; rowIndex++)
            {
                string[] oldCells = rowIndex < oldRows.Count ? AxisToCells(oldRows[rowIndex]) : Array.Empty<string>();
                string[] newCells = rowIndex < newRows.Count ? AxisToCells(newRows[rowIndex]) : Array.Empty<string>();
                int maxCols = Math.Max(oldCells.Length, newCells.Length);
                for (int colIndex = 0; colIndex < maxCols; colIndex++)
                {
                    string oldValue = GetCell(oldCells, colIndex);
                    string newValue = GetCell(newCells, colIndex);
                    if (oldValue.Equals(newValue, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    changes.Add(new AxisChange
                    {
                        RowIndex = rowIndex + 1,
                        ColumnName = colIndex < AxisHeaders.Length ? AxisHeaders[colIndex] : "Column" + (colIndex + 1).ToString(CultureInfo.InvariantCulture),
                        OldValue = oldValue,
                        NewValue = newValue,
                    });
                }
            }

            return changes;
        }

        private static string[] AxisToCells(AxisParameterRecord row)
        {
            return new[]
            {
                row.CardId ?? string.Empty,
                row.AxisId ?? string.Empty,
                row.AxisName ?? string.Empty,
                row.Pitch ?? string.Empty,
                row.Pulses ?? string.Empty,
                row.Ppr ?? string.Empty,
                row.HomingAcc ?? string.Empty,
                row.HomingDeAcc ?? string.Empty,
                row.HomingSpeed ?? string.Empty,
            };
        }

        private static string GetCell(string[] row, int index)
        {
            if (row == null || index < 0 || index >= row.Length)
            {
                return string.Empty;
            }

            return row[index] ?? string.Empty;
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private string GetSettingsPath(string fileName)
        {
            return Path.Combine(_dataDirectory, SettingsFolder, fileName);
        }

        private static string ReadElement(XElement node, string elementName)
        {
            var element = node.Element(elementName);
            return element == null ? string.Empty : (element.Value ?? string.Empty).Trim();
        }

        private static string ReadAttribute(XElement node, string attributeName)
        {
            var attribute = node.Attribute(attributeName);
            return attribute == null ? string.Empty : (attribute.Value ?? string.Empty).Trim();
        }

        private static string GetAeFileName(AeLimitKind kind)
        {
            switch (kind)
            {
                case AeLimitKind.Production:
                    return AeProductionFile;
                case AeLimitKind.Cpk:
                    return AeCpkFile;
                case AeLimitKind.Grr:
                    return AeGrrFile;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported AE limit file kind.");
            }
        }

        private static string GetAeNodeName(AeLimitKind kind)
        {
            switch (kind)
            {
                case AeLimitKind.Production:
                    return "AE_Limits_Production";
                case AeLimitKind.Cpk:
                    return "AE_Limits_CPK";
                case AeLimitKind.Grr:
                    return "AE_Limits_GRR";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported AE limit XML node.");
            }
        }

        private XElement LoadXmlRoot(string path)
        {
            var doc = LoadXmlDocument(path);
            return GetRequiredRoot(doc);
        }

        private XDocument LoadXmlDocument(string path)
        {
            EnsureExistingFile(path);
            var doc = XDocument.Load(path);
            if (doc.Root == null)
            {
                throw new InvalidDataException("XML file has no root element: " + path);
            }

            return doc;
        }

        private static XElement GetRequiredRoot(XDocument doc)
        {
            if (doc.Root == null)
            {
                throw new InvalidDataException("XML file has no root element.");
            }

            return doc.Root;
        }

        private static string NormalizeXml(XDocument doc)
        {
            return doc.ToString(SaveOptions.DisableFormatting).Trim();
        }

        private static void SaveXml(string path, XDocument doc)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(true),
                Indent = true,
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace,
            };

            using (var writer = XmlWriter.Create(path, settings))
            {
                doc.Save(writer);
            }
        }

        private static void EnsureExistingFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Settings file not found.", path);
            }
        }

        private sealed class AxisChange
        {
            public int RowIndex { get; set; }
            public string ColumnName { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
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
    }
}
