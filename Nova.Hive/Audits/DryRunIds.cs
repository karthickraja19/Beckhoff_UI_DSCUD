using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Nova.Hive
{
    /// <summary>What UUID.dll gives for one unit of a 24-hour dry run.</summary>
    public sealed class DryRunUnit
    {
        public DateTime InputTime { get; set; }

        public string UnitSn { get; set; }

        public string MacAddress { get; set; }
    }

    /// <summary>What UMSID.dll gives for one machine state change of a 24-hour dry run.</summary>
    public sealed class DryRunState
    {
        public DateTime StateChangeTime { get; set; }

        public string UniqueMachineStateId { get; set; }
    }

    /// <summary>
    /// The two DLLs of FAT remote qualification (S4.1 "Extra Log for Hive Remote
    /// Qualification"; "FAT Remote Qualification DLL Quick Start Guide" V4.0, RQ DLL_X64
    /// V2.0.0.1). While the 24-hour dry run is on:
    /// <list type="bullet">
    /// <item>unit_sn, input_time and mac_addr of every machine_data payload "must be strictly
    /// obtained from UUID.DLL";</item>
    /// <item>unique_machine_state_id of every machine_state payload "must be strictly obtained
    /// from UMSID.DLL".</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Both are called with the SI code, the machine state and the previous state, exactly as
    /// the guide's C# example does through its LibXdll wrapper: the SI code as an ANSI string,
    /// the answer read back as an ANSI string. UMSID.dll answers
    /// <c>state_change_time&amp;&amp;unique_machine_state_id</c>; UUID.dll answers
    /// <c>input_time&amp;&amp;unit_sn&amp;&amp;mac_addr</c>.
    /// The DLLs are 64 bit and have to lie beside the exe, with the C++ runtime DLLs the
    /// package ships (msvcp140, vcruntime140, vcruntime140_1, ucrtbase). They are not part of
    /// this repository.
    /// </remarks>
    public static class DryRunIds
    {
        public const string UuidDll = "UUID.dll";
        public const string UmsidDll = "UMSID.dll";

        private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffzzz";

        [DllImport(UmsidDll, EntryPoint = "GenerateUmsid", CharSet = CharSet.Auto)]
        private static extern IntPtr GenerateUmsid(IntPtr siCode, int machineState, int previousState);

        [DllImport(UuidDll, EntryPoint = "GenerateUuid", CharSet = CharSet.Auto)]
        private static extern IntPtr GenerateUuid(IntPtr siCode, int machineState, int previousState);

        /// <summary>
        /// Why the dry run cannot be used on this machine - no SI code, a 32 bit process, a
        /// DLL not beside the exe - or null when everything it needs is there.
        /// </summary>
        public static string UnavailableReason(string siCode)
        {
            if (string.IsNullOrWhiteSpace(siCode))
                return "The SI code is not set (HiveDryRunSiCode in App.config).";

            if (!Environment.Is64BitProcess)
                return "The remote qualification DLLs are 64 bit and this software is running as a 32 bit process.";

            string folder = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string dll in new[] { UuidDll, UmsidDll })
            {
                if (!File.Exists(Path.Combine(folder, dll)))
                    return dll + " is not beside the software (" + folder + "). Copy the RQ DLL package's C# Release files there.";
            }

            return null;
        }

        /// <summary>UMSID.dll: the state change time and the unique machine state id. Throws with the reason when it cannot.</summary>
        public static DryRunState MachineState(string siCode, int machineState, int previousState)
        {
            string answer = Call(GenerateUmsid, siCode, machineState, previousState, UmsidDll);
            string[] parts = Split(answer, 2, UmsidDll);
            return new DryRunState
            {
                StateChangeTime = ParseTime(parts[0], UmsidDll),
                UniqueMachineStateId = parts[1],
            };
        }

        /// <summary>UUID.dll: the unit's input time, serial number and MAC address. Throws with the reason when it cannot.</summary>
        public static DryRunUnit Unit(string siCode, int machineState, int previousState)
        {
            string answer = Call(GenerateUuid, siCode, machineState, previousState, UuidDll);
            string[] parts = Split(answer, 3, UuidDll);
            return new DryRunUnit
            {
                InputTime = ParseTime(parts[0], UuidDll),
                UnitSn = parts[1],
                MacAddress = parts[2],
            };
        }

        private static string Call(Func<IntPtr, int, int, IntPtr> generate, string siCode, int machineState, int previousState, string dll)
        {
            IntPtr code = Marshal.StringToHGlobalAnsi(siCode ?? string.Empty);
            try
            {
                // The returned string is the DLL's own: read, never freed here.
                IntPtr answer = generate(code, machineState, previousState);
                string text = answer == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(answer);
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException(dll + " gave no answer.");

                return text.Trim();
            }
            catch (DllNotFoundException ex)
            {
                throw new InvalidOperationException(dll + " (or a C++ runtime DLL it needs) could not be loaded: " + ex.Message, ex);
            }
            catch (BadImageFormatException ex)
            {
                throw new InvalidOperationException(dll + " is not a 64 bit DLL this process can load: " + ex.Message, ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new InvalidOperationException(dll + " is not the expected version (RQ DLL 2.0.0.1): " + ex.Message, ex);
            }
            finally
            {
                Marshal.FreeHGlobal(code);
            }
        }

        private static string[] Split(string answer, int count, string dll)
        {
            string[] parts = answer.Split(new[] { "&&" }, StringSplitOptions.None);
            if (parts.Length != count)
                throw new InvalidOperationException(dll + " answered \"" + answer + "\", which is not " + count + " values joined by &&.");

            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim();
                if (parts[i].Length == 0)
                    throw new InvalidOperationException(dll + " answered \"" + answer + "\" with an empty value.");
            }

            return parts;
        }

        /// <summary>"2025-09-01T09:55:22.406+0800".</summary>
        private static DateTime ParseTime(string text, string dll)
        {
            // .NET wants the offset as +08:00.
            string withColon = text.Length > 5 && (text[text.Length - 5] == '+' || text[text.Length - 5] == '-')
                ? text.Substring(0, text.Length - 2) + ":" + text.Substring(text.Length - 2)
                : text;

            DateTimeOffset time;
            if (!DateTimeOffset.TryParseExact(withColon, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                throw new InvalidOperationException(dll + " gave a time that cannot be read: \"" + text + "\".");

            return time.LocalDateTime;
        }
    }
}
