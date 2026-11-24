using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Services
{
    /// <summary>One editable row of the Config Description tab.</summary>
    public sealed class DescriptionEntry
    {
        /// <summary>The point or parameter name, e.g. "Main_Air_Press_Fb" - never changed.</summary>
        public string Key { get; set; }

        /// <summary>Input / Output for I/O points, the limit set for spec limits.</summary>
        public string Category { get; set; }

        /// <summary>Shown beside the key to identify the row, e.g. the I/O address "X0-00".</summary>
        public string Context { get; set; }

        /// <summary>English description.</summary>
        public string English { get; set; }

        /// <summary>Description in the language being edited; empty falls back to English.</summary>
        public string Translation { get; set; }
    }

    /// <summary>
    /// Per-language names for machine data that is not in the language files: the I/O
    /// points of Data\Config.csv (Description_English, Description_zh-CN, Description_vi
    /// columns, as Odyssey.Master_V3) and the spec limit parameters
    /// (Data\Settings\SpecLimitDescriptions.csv).
    ///
    /// The screens show <see cref="PointName"/> / <see cref="SpecLimitName"/>; the name
    /// itself stays what the machine, the PLC and the recipes use.
    /// </summary>
    public interface IDescriptionService
    {
        /// <summary>"I/O Points" and "Spec Limits".</summary>
        IReadOnlyList<string> Sources { get; }

        /// <summary>The I/O point in the active language, its English description, or its name.</summary>
        string PointName(string pointName);

        /// <summary>The spec limit parameter in the active language, its English description, or its name.</summary>
        string SpecLimitName(string parameter);

        /// <summary>Every row of <paramref name="source"/>, with English and <paramref name="culture"/>.</summary>
        IReadOnlyList<DescriptionEntry> Load(string source, string culture);

        /// <summary>Writes the rows back to the file the screens read. Throws on failure.</summary>
        void Save(string source, string culture, IEnumerable<DescriptionEntry> entries);

        /// <summary>The file behind <paramref name="source"/>, for the editor to show.</summary>
        string FilePathOf(string source);

        /// <summary>Raised (UI thread) after a save or a language change.</summary>
        event EventHandler Changed;
    }
}
