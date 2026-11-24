using System;
using System.Collections.Generic;
using System.Linq;

namespace Nova.Wpf.App.Services.Twincat.Mapping
{
    /// <summary>One PLC variable of a device block, relative to the device's path.</summary>
    public sealed class DeviceMemberSymbol
    {
        /// <summary>Relative to the device, e.g. "hmicmd.bstart" or "status.Actualweight".</summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>BOOL, SINT, USINT, INT, UINT, DINT, UDINT, REAL, LREAL, TIME, or STRING(n).</summary>
        public string Type { get; set; } = "BOOL";

        /// <summary>PLC value = HMI value x Scale (the dispenser takes 9.7 as 97: Scale 10).</summary>
        public double Scale { get; set; } = 1;

        /// <summary>For a command button: how long it stays TRUE, ms; 0 takes the block's pulse length.</summary>
        public int PulseMilliseconds { get; set; }
    }

    /// <summary>One device of a kind: its name on screen and the path of its PLC data block.</summary>
    public sealed class DeviceInstanceSymbol
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>E.g. MAIN.dispenser.dispenserdata[1].</summary>
        public string Path { get; set; } = string.Empty;
    }

    /// <summary>
    /// One kind of third-party device: its instances and the members of its data block, by
    /// the logical key its Manual tab uses. A key left out (or blank) is not offered.
    /// </summary>
    public sealed class DeviceBlockSymbols
    {
        public List<DeviceInstanceSymbol> Instances { get; set; } = new List<DeviceInstanceSymbol>();

        public Dictionary<string, DeviceMemberSymbol> Members { get; set; }
            = new Dictionary<string, DeviceMemberSymbol>(StringComparer.OrdinalIgnoreCase);

        /// <summary>How long a command button stays TRUE, ms.</summary>
        public int PulseMilliseconds { get; set; } = 200;

        public IEnumerable<DeviceInstanceSymbol> MappedInstances =>
            (Instances ?? new List<DeviceInstanceSymbol>()).Where(i => i != null && PlcSymbolMap.Has(i.Path));

        public DeviceMemberSymbol Member(string key)
        {
            DeviceMemberSymbol member;
            return key != null && Members != null && Members.TryGetValue(key, out member) && member != null && PlcSymbolMap.Has(member.Symbol)
                ? member
                : null;
        }
    }

    /// <summary>The third-party devices on the Manual tab.</summary>
    public sealed class ThirdPartyDeviceSymbols
    {
        public DeviceBlockSymbols Dispenser { get; set; } = new DeviceBlockSymbols();

        public DeviceBlockSymbols Weighing { get; set; } = new DeviceBlockSymbols();

        public DeviceBlockSymbols Vibration { get; set; } = new DeviceBlockSymbols();

        public IEnumerable<KeyValuePair<string, string>> NamedSymbols()
        {
            foreach (var kind in new[]
            {
                new KeyValuePair<string, DeviceBlockSymbols>("Dispenser", Dispenser),
                new KeyValuePair<string, DeviceBlockSymbols>("Weighing", Weighing),
                new KeyValuePair<string, DeviceBlockSymbols>("Vibration", Vibration),
            })
            {
                if (kind.Value == null)
                    continue;

                foreach (DeviceInstanceSymbol instance in kind.Value.MappedInstances)
                {
                    foreach (var member in kind.Value.Members ?? new Dictionary<string, DeviceMemberSymbol>())
                    {
                        if (member.Value != null && PlcSymbolMap.Has(member.Value.Symbol))
                        {
                            yield return new KeyValuePair<string, string>(
                                "ThirdPartyDevices." + kind.Key + "." + instance.Name + "." + member.Key,
                                instance.Path.Trim() + "." + member.Value.Symbol.Trim());
                        }
                    }
                }
            }
        }
    }
}
