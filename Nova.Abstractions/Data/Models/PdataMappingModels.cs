using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Data.Models
{
    /// <summary>
    /// One selected PDATA field, as configured in the PDATA Mapping grid.
    /// Everything the query needs except SN and Suffix, which only exist at
    /// runtime (the unit being reported).
    /// </summary>
    public sealed class PdataFieldConfig
    {
        public string ParameterName { get; set; } = string.Empty;

        /// <summary>"pdata" for measurement fields, "attr" for SN-type fields.</summary>
        public string DataType { get; set; } = string.Empty;

        public string MinLimit { get; set; } = string.Empty;
        public string MaxLimit { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string Prefix { get; set; } = string.Empty;

        /// <summary>SN-type attributes are formatted without Min/Max in the query line.</summary>
        public bool IsSNField { get; set; }
    }

    /// <summary>
    /// The reusable query template written to PDATA\{machine}_template.json and read
    /// back when a unit is reported, which resolves SN and Suffix against the live unit.
    /// </summary>
    public sealed class PdataQueryTemplate
    {
        public string MachineName { get; set; } = string.Empty;
        public string GeneratedAt { get; set; } = string.Empty;

        /// <summary>When true, every field's Min/Max is written out empty.</summary>
        public bool IgnoreLimits { get; set; }

        public IReadOnlyList<PdataFieldConfig> Fields { get; set; } = Array.Empty<PdataFieldConfig>();
    }

    /// <summary>
    /// A saved machine profile: which PDATA fields are checked, the grid behind them,
    /// and the generated query lines. Persisted as PDATA\{machine}.json.
    /// </summary>
    public sealed class PdataMachineConfig
    {
        public string MachineName { get; set; } = string.Empty;
        public IReadOnlyList<string> SelectedFields { get; set; } = Array.Empty<string>();
        public IReadOnlyList<Dictionary<string, string>> Rows { get; set; } = Array.Empty<Dictionary<string, string>>();
        public IReadOnlyList<string> Query { get; set; } = Array.Empty<string>();
    }
}
