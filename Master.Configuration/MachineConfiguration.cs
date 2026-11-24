using Master.Configuration.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Teal.Library.Log;

namespace Master.Configuration
{
    /// <summary>
    /// Reads the machine's device configuration from the Data folder into
    /// <see cref="ConfigGlobalClass"/>, and writes back the positions taught on the recipe
    /// screens. The one place that knows these files and their format: the HMI and the
    /// *Objects loaders both read them through here.
    /// </summary>
    /// <remarks>
    /// Nothing here shows a message box or throws for a bad file. A file that is missing or
    /// cannot be read is reported in the list <see cref="Load"/> returns, for the caller to
    /// show, and its devices are left empty - one broken file must not stop the others loading.
    /// </remarks>
    public static class MachineConfiguration
    {
        public const string CardsFile = "CardsConfiguration.json";
        public const string ServosFile = "Servos.json";
        public const string SteppersFile = "Steppers.json";
        public const string CylindersFile = "Cylinders.json";
        public const string VacuumsFile = "Vacuums.json";
        public const string CcdsFile = "CCDs.json";
        public const string ScannersFile = "Scanners.json";
        public const string SfcFile = "SFC.json";
        public const string RobotsFile = "Robots.json";
        public const string PdcaFile = "Pdca.json";
        public const string GantryFile = "Gantry.json";

        /// <summary>The Data folder the configuration was last loaded from; null before <see cref="Load"/>.</summary>
        public static string DataDirectory { get; private set; }

        /// <summary>True once <see cref="Load"/> has run.</summary>
        public static bool IsLoaded
        {
            get { return DataDirectory != null; }
        }

        /// <summary>
        /// Reads every device file in <paramref name="dataDirectory"/> into ConfigGlobalClass.
        /// Returns the problems found; an empty list means everything was read.
        /// </summary>
        public static List<string> Load(string dataDirectory)
        {
            var problems = new List<string>();
            DataDirectory = dataDirectory;

            // The motion and pneumatic files describe the machine itself: a missing one is a problem.
            ConfigGlobalClass.ServoConfigurations = ReadList<ServoConfiguration>(ServosFile, true, problems);
            ConfigGlobalClass.StepperConfigurations = ReadList<StepperConfiguration>(SteppersFile, true, problems);
            ConfigGlobalClass.CylinderConfigurations = ReadList<CylinderConfiguration>(CylindersFile, true, problems);
            ConfigGlobalClass.VacuumConfigurations = ReadList<VacuumConfiguration>(VacuumsFile, true, problems);

            // A machine without one of these devices simply has no file for it.
            ConfigGlobalClass.CCDConfigurations = ReadList<VisionConfiguration>(CcdsFile, false, problems);
            ConfigGlobalClass.ScannerConfigurations = ReadList<NetworkDeviceConfiguration>(ScannersFile, false, problems);
            ConfigGlobalClass.SfcConfigurations = ReadList<SocketClientConfiguration>(SfcFile, false, problems);
            ConfigGlobalClass.RobotConfigurations = ReadList<RobotConfiguration>(RobotsFile, false, problems);
            ConfigGlobalClass.PdcaConfiguration = ReadList<SocketClientConfiguration>(PdcaFile, false, problems);

            ConfigGlobalClass.CardConfiguration = ReadCard(problems);

            // The enums list the machine's devices; each one's settings are the entry in its
            // file with the same EnumName. Where the two disagree, say so once, here.
            CheckNames<Servos, ServoConfiguration>(ServosFile, ConfigGlobalClass.ServoConfigurations, c => c.EnumName, problems);
            CheckNames<Steppers, StepperConfiguration>(SteppersFile, ConfigGlobalClass.StepperConfigurations, c => c.EnumName, problems);
            CheckNames<Cylinders, CylinderConfiguration>(CylindersFile, ConfigGlobalClass.CylinderConfigurations, c => c.EnumName, problems);
            CheckNames<Vacuums, VacuumConfiguration>(VacuumsFile, ConfigGlobalClass.VacuumConfigurations, c => c.EnumName, problems);
            CheckNames<CCDs, VisionConfiguration>(CcdsFile, ConfigGlobalClass.CCDConfigurations, c => c.EnumName, problems);
            CheckNames<Scanners, NetworkDeviceConfiguration>(ScannersFile, ConfigGlobalClass.ScannerConfigurations, c => c.EnumName, problems);
            CheckNames<Robots, RobotConfiguration>(RobotsFile, ConfigGlobalClass.RobotConfigurations, c => c.EnumName, problems);
            CheckGantry(ConfigGlobalClass.ServoConfigurations, problems);
            return problems;
        }

        /// <summary>The axis types a gantry module's axes can have, in E_Servo order (X_AXIS = 1 ... R_AXIS = 4).</summary>
        public static readonly string[] GantryAxisTypes = { "X", "Y", "Z", "R" };

        /// <summary>
        /// Reports where Gantry.json does not describe the machine: an axis that is not a servo
        /// in Servos.json, an axis type other than X, Y, Z or R, or two axes of the same type in
        /// one module (the PLC's recipe has one column per type). A machine without gantries
        /// has no Gantry.json, so a missing file is not a problem.
        /// </summary>
        private static void CheckGantry(List<ServoConfiguration> servos, List<string> problems)
        {
            if (!File.Exists(Path.Combine(DataDirectory, GantryFile)))
                return;

            GantryRoot root = LoadGantry(DataDirectory);
            if (root == null)
            {
                problems.Add("Data\\" + GantryFile + " could not be read, so the Recipe page has no gantry.");
                return;
            }

            foreach (GantryConfiguration module in root.Gantry ?? new List<GantryConfiguration>())
            {
                string name = string.IsNullOrWhiteSpace(module?.Name) ? "(a module with no Name)" : module.Name;
                if (module?.Axes == null || module.Axes.Count == 0)
                {
                    problems.Add("Data\\" + GantryFile + ": " + name + " has no axes.");
                    continue;
                }

                List<string> unknown = module.Axes
                    .Where(a => a == null || !servos.Any(s => s != null && string.Equals(s.EnumName, a.Name, StringComparison.OrdinalIgnoreCase)))
                    .Select(a => string.IsNullOrWhiteSpace(a?.Name) ? "(no Name)" : a.Name)
                    .ToList();
                if (unknown.Count > 0)
                {
                    problems.Add("Data\\" + GantryFile + ": " + name + " names axes that are not servos in Data\\" + ServosFile
                        + ", so they are left out: " + string.Join(", ", unknown) + ".");
                }

                List<string> badType = module.Axes
                    .Where(a => a != null && !GantryAxisTypes.Contains((a.Type ?? string.Empty).Trim().ToUpperInvariant()))
                    .Select(a => a.Name + " (\"" + a.Type + "\")")
                    .ToList();
                if (badType.Count > 0)
                {
                    problems.Add("Data\\" + GantryFile + ": " + name + " has axes whose Type is not X, Y, Z or R, so they are left out: "
                        + string.Join(", ", badType) + ".");
                }

                List<string> repeated = module.Axes
                    .Where(a => a != null)
                    .GroupBy(a => (a.Type ?? string.Empty).Trim().ToUpperInvariant())
                    .Where(g => g.Key.Length > 0 && g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();
                if (repeated.Count > 0)
                {
                    problems.Add("Data\\" + GantryFile + ": " + name + " has more than one axis of type " + string.Join(", ", repeated)
                        + ", so only the first usable one of each is used.");
                }
            }
        }

        /// <summary>
        /// Each member of <typeparamref name="TEnum"/> with its entry in <paramref name="configs"/> -
        /// the first with the member's name as its EnumName - in the enum's order. A member
        /// with no entry is left out; <see cref="Load"/> reports it.
        /// </summary>
        public static List<KeyValuePair<TEnum, TConfig>> Configured<TEnum, TConfig>(List<TConfig> configs, Func<TConfig, string> enumName)
            where TEnum : struct, Enum
            where TConfig : class
        {
            var configured = new List<KeyValuePair<TEnum, TConfig>>();
            foreach (TEnum member in (TEnum[])Enum.GetValues(typeof(TEnum)))
            {
                string name = member.ToString();
                TConfig config = configs.FirstOrDefault(c => c != null && enumName(c) == name);
                if (config != null)
                    configured.Add(new KeyValuePair<TEnum, TConfig>(member, config));
            }

            return configured;
        }

        /// <summary>
        /// The gantry modules and the servos each is made of (Gantry.json), or null when there
        /// is no such file or it cannot be read. A module's recipe is its servos' ConfiguredPositions.
        /// </summary>
        public static GantryRoot LoadGantry(string dataDirectory)
        {
            string path = Path.Combine(dataDirectory, GantryFile);
            if (!File.Exists(path))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<GantryRoot>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                LogError("Could not read " + path + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Servos.json as it is on disk now, for a caller that changes it and saves it back with
        /// <see cref="SaveServos"/>. Null when the file is missing or cannot be read.
        /// </summary>
        public static List<ServoConfiguration> ReadServos(string dataDirectory)
        {
            string path = Path.Combine(dataDirectory, ServosFile);
            if (!File.Exists(path))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<List<ServoConfiguration>>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                LogError("Could not read " + path + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>Writes Servos.json - the taught positions - and keeps ConfigGlobalClass in step when it was loaded from the same folder.</summary>
        public static void SaveServos(string dataDirectory, List<ServoConfiguration> servos)
        {
            string path = Path.Combine(dataDirectory, ServosFile);
            File.WriteAllText(path, JsonConvert.SerializeObject(servos, Formatting.Indented), Encoding.UTF8);

            if (string.Equals(DataDirectory, dataDirectory, StringComparison.OrdinalIgnoreCase))
                ConfigGlobalClass.ServoConfigurations = servos;
        }

        /// <summary>
        /// The Beckhoff card: the PLC's AMS Net ID and port, and where the device arrays live.
        /// The file used to hold the Advantech cards as a list; that shape is reported rather
        /// than half-read, so a machine still on the old file says so at startup.
        /// </summary>
        private static CardConfiguration ReadCard(List<string> problems)
        {
            string path = Path.Combine(DataDirectory, CardsFile);
            if (!File.Exists(path))
            {
                problems.Add("Data\\" + CardsFile + " is missing, so there is no PLC to connect to.");
                return null;
            }

            try
            {
                JToken token = JToken.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (token.Type != JTokenType.Object)
                {
                    problems.Add("Data\\" + CardsFile + " is not a Beckhoff card configuration (it holds a list - the old "
                        + "Advantech format). It needs one object with ConnectionId, PortNo, DBName and StepperDUTName.");
                    return null;
                }

                var card = token.ToObject<CardConfiguration>();
                if (string.IsNullOrWhiteSpace(card.ConnectionId) || card.PortNo <= 0)
                {
                    problems.Add("Data\\" + CardsFile + " needs the PLC's ConnectionId (AMS Net ID) and PortNo.");
                    return null;
                }

                return card;
            }
            catch (Exception ex)
            {
                problems.Add("Data\\" + CardsFile + " could not be read: " + ex.Message);
                LogError("Could not read " + path + ": " + ex);
                return null;
            }
        }

        /// <summary>
        /// Reports where a device file and its enum disagree: entries that are not members of
        /// the enum, members with no entry, and members with more than one. Such entries and
        /// members are left out of the devices. A missing file is reported when it is read, so
        /// it is not checked.
        /// </summary>
        private static void CheckNames<TEnum, TConfig>(string fileName, List<TConfig> configs, Func<TConfig, string> enumName, List<string> problems)
            where TEnum : struct, Enum
            where TConfig : class
        {
            if (!File.Exists(Path.Combine(DataDirectory, fileName)))
                return;

            string enumType = typeof(TEnum).Name;
            string[] members = Enum.GetNames(typeof(TEnum));
            List<string> names = configs.Where(c => c != null).Select(enumName).ToList();

            List<string> unknown = names.Where(n => !members.Contains(n))
                .Select(n => string.IsNullOrWhiteSpace(n) ? "(no EnumName)" : n)
                .Distinct()
                .ToList();
            if (unknown.Count > 0)
            {
                problems.Add("Data\\" + fileName + " has entries that are not in the " + enumType
                    + " enum, so they are left out: " + string.Join(", ", unknown) + ".");
            }

            List<string> missing = members.Where(m => !names.Contains(m)).ToList();
            if (missing.Count > 0)
            {
                problems.Add("Data\\" + fileName + " has no entry for these " + enumType
                    + ", so they are left out: " + string.Join(", ", missing) + ".");
            }

            List<string> repeated = members.Where(m => names.Count(n => n == m) > 1).ToList();
            if (repeated.Count > 0)
            {
                problems.Add("Data\\" + fileName + " has more than one entry for these " + enumType
                    + ", so the first is used: " + string.Join(", ", repeated) + ".");
            }
        }

        private static List<T> ReadList<T>(string fileName, bool required, List<string> problems)
        {
            string path = Path.Combine(DataDirectory, fileName);
            if (!File.Exists(path))
            {
                if (required)
                    problems.Add("Data\\" + fileName + " is missing, so those devices are not shown.");
                return new List<T>();
            }

            try
            {
                return JsonConvert.DeserializeObject<List<T>>(File.ReadAllText(path, Encoding.UTF8)) ?? new List<T>();
            }
            catch (Exception ex)
            {
                problems.Add("Data\\" + fileName + " could not be read, so those devices are not shown: " + ex.Message);
                LogError("Could not read " + path + ": " + ex);
                return new List<T>();
            }
        }

        private static void LogError(string message)
        {
            Logger.WriteLog(LogType.Error, "Configuration", "MachineConfiguration", message, "");
        }
    }
}
