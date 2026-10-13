using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nova.Abstractions.Services
{
    /// <summary>One hash key of Audio SW DFM v5.4 slide 12, e.g. MS_SHA1 for the main software.</summary>
    public sealed class HashKeyDefinition
    {
        public HashKeyDefinition(string key, string controls)
        {
            Key = key;
            Controls = controls;
        }

        /// <summary>The HIVE key, e.g. "CD3_SHA1".</summary>
        public string Key { get; }

        /// <summary>What the file is, e.g. "Glue Path Files".</summary>
        public string Controls { get; }
    }

    /// <summary>One key as it was read: its file, and the hash or why there is none.</summary>
    public sealed class HashKeyReading
    {
        public string Key { get; set; }

        public string FilePath { get; set; }

        /// <summary>40 upper-case hex characters; null when it could not be read.</summary>
        public string Value { get; set; }

        /// <summary>Why <see cref="Value"/> is missing: no such file, no such folder, not 40 hex characters.</summary>
        public string Error { get; set; }

        public bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(FilePath); }
        }
    }

    /// <summary>
    /// The hash keys HIVE gets with every machine_state payload (slide 12) and the PD-15
    /// hash-key page (S3.6): which file each key tracks, their live values, the CSV
    /// export and the upload to MES.
    /// </summary>
    public interface IHashKeyService
    {
        /// <summary>Every key the DFM defines, in the slide's order.</summary>
        IReadOnlyList<HashKeyDefinition> Definitions { get; }

        /// <summary>The file set for each key; a key with no file is not monitored.</summary>
        IReadOnlyDictionary<string, string> Paths { get; }

        /// <summary>Saves the file paths (Level 3, PD-15).</summary>
        bool SavePaths(IDictionary<string, string> paths, out string error);

        /// <summary>
        /// Reads every configured file now ("Refresh all hash keys"). Keys without a file
        /// are left out.
        /// </summary>
        IReadOnlyList<HashKeyReading> ReadAll();

        /// <summary>The configured keys and their values, for the machine_state payload.</summary>
        IReadOnlyList<HashKeyReading> Latest { get; }

        /// <summary>Writes key, file and value to a CSV ("Export all hash keys").</summary>
        bool ExportCsv(string path, out string error);

        /// <summary>
        /// "Upload to MES" (SAVE_AUDIO_INFO): refreshes MES's copy of this station's keys.
        /// </summary>
        /// <param name="approverId">The badge of the second approver, sent as approver_id.</param>
        Task<MesResult> UploadToMesAsync(string approverId);

        /// <summary>Raised after the paths were saved.</summary>
        event EventHandler PathsChanged;
    }
}
