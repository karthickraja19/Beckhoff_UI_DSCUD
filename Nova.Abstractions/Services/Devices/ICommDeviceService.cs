using System.Collections.Generic;
using Nova.Abstractions.Hardware.Interfaces;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// The kinds of TCP-connected external devices the Manual console tabs drive. The robots
    /// are on EtherCAT, not here: see <see cref="IRobotIOService"/>.
    /// </summary>
    public enum CommDeviceKind
    {
        Ccd,
        Scanner,
        Sfc
    }

    /// <summary>
    /// One selectable console device: display info plus the network transport that
    /// backs it.
    /// </summary>
    public sealed class CommDevice
    {
        public string Name { get; }
        public string Detail { get; }
        public string DefaultCommand { get; }
        public INetworkProtocol Protocol { get; }

        public CommDevice(string name, string detail, string defaultCommand, INetworkProtocol protocol)
        {
            Name = name;
            Detail = detail;
            DefaultCommand = defaultCommand;
            Protocol = protocol;
        }
    }

    /// <summary>
    /// Registry of TCP comm devices (CCD, Scanner, SFC), config-driven from
    /// the machine's *.json files. The console ViewModels resolve their device list
    /// and per-device transport through this.
    /// </summary>
    public interface ICommDeviceService
    {
        IReadOnlyList<CommDevice> GetDevices(CommDeviceKind kind);
    }
}
