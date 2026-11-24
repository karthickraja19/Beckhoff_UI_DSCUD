using System;
using System.IO;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Hive
{
    /// <summary>
    /// Keeps the last HIVE state in C:\Hive\LatestHiveState.ini, as the sequence logic
    /// document asks. The state is written only after the gateway has accepted it, so the
    /// file always matches what HIVE knows, and it is read at startup so the machine
    /// carries on in the state it was left in.
    /// </summary>
    /// <remarks>
    /// File format:
    /// <code>
    /// [machine_state]
    /// sequence_0=5
    /// </code>
    /// </remarks>
    public sealed class LatestHiveStateFile
    {
        private readonly string _file;

        public LatestHiveStateFile(string file)
        {
            _file = file;
        }

        /// <summary>The state written last time, or <paramref name="defaultState"/> if there is none.</summary>
        public int Read(int sequence, int defaultState)
        {
            try
            {
                if (!File.Exists(_file))
                    return defaultState;

                string wanted = "sequence_" + sequence + "=";

                foreach (string line in File.ReadAllLines(_file))
                {
                    string text = line.Trim();
                    if (!text.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int state;
                    if (int.TryParse(text.Substring(wanted.Length).Trim(), out state))
                        return state;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "StateFile", "Could not read " + _file + ": " + ex.Message, ex.ToString());
            }

            return defaultState;
        }

        /// <summary>Writes the state the gateway has just accepted.</summary>
        public void Save(int sequence, int state)
        {
            try
            {
                string folder = Path.GetDirectoryName(_file);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);

                File.WriteAllText(_file, "[machine_state]" + Environment.NewLine + "sequence_" + sequence + "=" + state + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "StateFile", "Could not write " + _file + ": " + ex.Message, ex.ToString());
            }
        }
    }
}
