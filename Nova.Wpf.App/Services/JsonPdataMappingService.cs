using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// <see cref="IPdataMappingService"/> over the PDATA folder beside the exe.
    /// </summary>
    /// <remarks>
    /// The field catalogue is Data\Settings\PdataFields.json ({ "fields": [ ... ] }): the keys
    /// a unit's record can carry (the HIVE machine_data keys and the machine parameters of the
    /// MES naming convention). Profiles are PDATA\{machine}.json and the query template
    /// PDATA\{machine}_template.json; both keep the JSON shape the legacy V3 Settings tab
    /// wrote, so a profile saved there opens here.
    /// </remarks>
    public sealed class JsonPdataMappingService : IPdataMappingService
    {
        private const string DefaultMachineName = "TE010";

        private readonly IDialogService _dialogs;
        private readonly IFileDialogService _files;
        private readonly string _catalogueFile;

        public JsonPdataMappingService(IDialogService dialogs, IFileDialogService files, string dataDirectory)
        {
            _dialogs = dialogs;
            _files = files;
            _catalogueFile = Path.Combine(dataDirectory, "Settings", "PdataFields.json");
        }

        private static string PdataDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDATA");

        public IReadOnlyList<string> GetAvailableFields()
        {
            try
            {
                if (File.Exists(_catalogueFile))
                {
                    CatalogueDto catalogue = JsonConvert.DeserializeObject<CatalogueDto>(File.ReadAllText(_catalogueFile));
                    List<string> names = (catalogue?.Fields ?? new List<string>())
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(n => n.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (names.Count > 0)
                        return names;
                }
            }
            catch (Exception ex)
            {
                _dialogs?.ShowWarning("Could not read " + _catalogueFile + ": " + ex.Message, "PDATA Mapping");
            }

            return new List<string>();
        }

        public IReadOnlyList<string> GetMachineNames()
        {
            try
            {
                if (Directory.Exists(PdataDir))
                {
                    List<string> names = Directory.GetFiles(PdataDir, "*.json")
                        .Select(Path.GetFileNameWithoutExtension)
                        // The folder also holds the generated template; it is not a profile.
                        .Where(n => !n.EndsWith("_template", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(n => n)
                        .ToList();

                    if (names.Count > 0)
                        return names;
                }
            }
            catch (Exception ex)
            {
                _dialogs?.ShowWarning("Could not list PDATA profiles: " + ex.Message, "PDATA Mapping");
            }

            return new List<string> { DefaultMachineName };
        }

        public PdataMachineConfig LoadMachineConfig(string machineName)
        {
            if (string.IsNullOrWhiteSpace(machineName))
                return null;

            string path = Path.Combine(PdataDir, MakeSafeFileName(machineName) + ".json");
            if (!File.Exists(path))
                return null;

            try
            {
                MachineConfigDto file = JsonConvert.DeserializeObject<MachineConfigDto>(File.ReadAllText(path));
                if (file == null)
                    return null;

                return new PdataMachineConfig
                {
                    MachineName = machineName,
                    SelectedFields = file.Pdata ?? new List<string>(),
                    Rows = (file.GridRows ?? new List<GridRowDto>()).Select(r => r.ToDictionary()).ToList(),
                    Query = file.Query ?? new List<string>(),
                };
            }
            catch (Exception ex)
            {
                _dialogs?.ShowError("Could not read PDATA profile '" + machineName + "': " + ex.Message, "PDATA Mapping");
                return null;
            }
        }

        public RecipeSaveResult SaveMachineConfig(PdataMachineConfig config)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.MachineName))
            {
                _dialogs?.ShowError("No machine name was supplied.", "PDATA Mapping");
                return null;
            }

            try
            {
                Directory.CreateDirectory(PdataDir);
                string path = Path.Combine(PdataDir, MakeSafeFileName(config.MachineName) + ".json");

                var dto = new MachineConfigDto
                {
                    Pdata = config.SelectedFields?.ToList() ?? new List<string>(),
                    GridRows = (config.Rows ?? new List<Dictionary<string, string>>()).Select(GridRowDto.From).ToList(),
                    Query = config.Query?.ToList() ?? new List<string>(),
                };

                File.WriteAllText(path, JsonConvert.SerializeObject(dto, Formatting.Indented));
                return RecipeSaveResult.Saved("Saved successfully.");
            }
            catch (Exception ex)
            {
                // null rather than a "saved" result keeps the tab in edit mode, so a failed
                // write never looks like a successful one.
                _dialogs?.ShowError("Could not save PDATA profile: " + ex.Message, "PDATA Mapping");
                return null;
            }
        }

        public void SaveQueryTemplate(PdataQueryTemplate template)
        {
            if (template == null || string.IsNullOrWhiteSpace(template.MachineName))
                return;

            try
            {
                Directory.CreateDirectory(PdataDir);
                string path = Path.Combine(PdataDir, MakeSafeFileName(template.MachineName) + "_template.json");

                var dto = new TemplateDto
                {
                    MachineName = template.MachineName,
                    GeneratedAt = template.GeneratedAt,
                    IgnoreLimits = template.IgnoreLimits,
                    Fields = (template.Fields ?? new List<PdataFieldConfig>()).Select(f => new TemplateFieldDto
                    {
                        ParameterName = f.ParameterName ?? string.Empty,
                        DataType = f.DataType ?? string.Empty,
                        MinLimit = f.MinLimit ?? string.Empty,
                        MaxLimit = f.MaxLimit ?? string.Empty,
                        Unit = f.Unit ?? string.Empty,
                        Prefix = f.Prefix ?? string.Empty,
                        IsSNField = f.IsSNField,
                    }).ToList(),
                };

                File.WriteAllText(path, JsonConvert.SerializeObject(dto, Formatting.Indented));
            }
            catch (Exception ex)
            {
                _dialogs?.ShowError("Could not save the PDATA query template: " + ex.Message, "PDATA Mapping");
            }
        }

        public IReadOnlyList<string> ImportFieldNamesFromCsv()
        {
            if (_files == null)
                return null;

            string path = _files.OpenFile("Import PDATA from CSV", "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*");
            if (path == null)
                return null;                            // cancelled

            try
            {
                var names = new List<string>();
                foreach (string line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    foreach (string token in line.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string clean = token.Trim('"', '\'', ' ');
                        if (clean.Length > 0)
                            names.Add(clean);
                    }
                }
                return names;
            }
            catch (Exception ex)
            {
                _dialogs?.ShowError("Error reading CSV:" + Environment.NewLine + ex.Message, "Import Error");
                return new List<string>();
            }
        }

        private static string MakeSafeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = (name ?? string.Empty).ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                    chars[i] = '_';
            }
            return new string(chars);
        }

        // ---- on-disk shapes ----------------------------------------------------------

        private sealed class CatalogueDto
        {
            [JsonProperty("fields")]
            public List<string> Fields { get; set; }
        }

        private sealed class MachineConfigDto
        {
            [JsonProperty("pdata")]
            public List<string> Pdata { get; set; }

            [JsonProperty("gridRows")]
            public List<GridRowDto> GridRows { get; set; }

            [JsonProperty("query")]
            public List<string> Query { get; set; }
        }

        private sealed class GridRowDto
        {
            [JsonProperty("prefix")] public string Prefix { get; set; } = string.Empty;
            [JsonProperty("datatype")] public string Datatype { get; set; } = string.Empty;
            [JsonProperty("placeholder")] public string Placeholder { get; set; } = string.Empty;
            [JsonProperty("suffix")] public string Suffix { get; set; } = string.Empty;
            [JsonProperty("minLimit")] public string MinLimit { get; set; } = string.Empty;
            [JsonProperty("maxLimit")] public string MaxLimit { get; set; } = string.Empty;
            [JsonProperty("unit")] public string Unit { get; set; } = string.Empty;

            public Dictionary<string, string> ToDictionary()
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["colPrefix"] = Prefix ?? string.Empty,
                    ["colDatatype"] = Datatype ?? string.Empty,
                    ["colPlaceholder"] = Placeholder ?? string.Empty,
                    ["colSuffix"] = Suffix ?? string.Empty,
                    ["colMinLimit"] = MinLimit ?? string.Empty,
                    ["colMaxLimit"] = MaxLimit ?? string.Empty,
                    ["colUnit"] = Unit ?? string.Empty,
                };
            }

            public static GridRowDto From(Dictionary<string, string> row)
            {
                return new GridRowDto
                {
                    Prefix = Get(row, "colPrefix"),
                    Datatype = Get(row, "colDatatype"),
                    Placeholder = Get(row, "colPlaceholder"),
                    Suffix = Get(row, "colSuffix"),
                    MinLimit = Get(row, "colMinLimit"),
                    MaxLimit = Get(row, "colMaxLimit"),
                    Unit = Get(row, "colUnit"),
                };
            }

            private static string Get(Dictionary<string, string> row, string key)
            {
                string v;
                return row != null && row.TryGetValue(key, out v) ? v ?? string.Empty : string.Empty;
            }
        }

        private sealed class TemplateDto
        {
            [JsonProperty("machineName")] public string MachineName { get; set; } = string.Empty;
            [JsonProperty("generatedAt")] public string GeneratedAt { get; set; } = string.Empty;
            [JsonProperty("ignoreLimits")] public bool IgnoreLimits { get; set; }
            [JsonProperty("fields")] public List<TemplateFieldDto> Fields { get; set; } = new List<TemplateFieldDto>();
        }

        private sealed class TemplateFieldDto
        {
            [JsonProperty("parameterName")] public string ParameterName { get; set; } = string.Empty;
            [JsonProperty("dataType")] public string DataType { get; set; } = string.Empty;
            [JsonProperty("minLimit")] public string MinLimit { get; set; } = string.Empty;
            [JsonProperty("maxLimit")] public string MaxLimit { get; set; } = string.Empty;
            [JsonProperty("unit")] public string Unit { get; set; } = string.Empty;
            [JsonProperty("prefix")] public string Prefix { get; set; } = string.Empty;
            [JsonProperty("isSNField")] public bool IsSNField { get; set; }
        }
    }
}
