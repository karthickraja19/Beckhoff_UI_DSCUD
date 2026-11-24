using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Data.Models
{
    public enum ColumnDataType
    {
        Text = 0,
        Numeric = 1,
        NonNegative = 2,
        Integer = 3,

        /// <summary>An IPv4 address: four numbers of 0-255 separated by dots.</summary>
        IpAddress = 4
    }

    public sealed class DynamicColumnSchema
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool IsReadOnly { get; set; }
        public bool IsIdentifier { get; set; }
        public ColumnDataType DataType { get; set; } = ColumnDataType.Text;
        public bool ValidationEnabled { get; set; } = true;
        public double? Width { get; set; }

        /// <summary>
        /// True for an IP address column: one typed <see cref="ColumnDataType.IpAddress"/>, or
        /// one keyed IP / IpAddress, whatever type its settings file gives it (FAM 2 SET_05).
        /// </summary>
        public bool IsIpAddress =>
            DataType == ColumnDataType.IpAddress
            || string.Equals(Key, "IpAddress", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Key, "IP", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class DynamicTableDocument
    {
        public string Title { get; set; } = "Robot";
        public bool HasMissingColumnSchema { get; set; }
        public string MissingSchemaMessage { get; set; } = string.Empty;
        public IReadOnlyList<DynamicColumnSchema> Columns { get; set; } = Array.Empty<DynamicColumnSchema>();
        public IReadOnlyList<Dictionary<string, string>> Rows { get; set; } = Array.Empty<Dictionary<string, string>>();
    }
}

