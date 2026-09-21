using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    /// <summary>
    /// A socket-connected external module (vision, robot, scanner) looked up through
    /// <see cref="IDeviceRegistry"/>. Placeholder contract: the transport surface is
    /// filled in when the Nova.CommAPI bindings land.
    /// </summary>
    public interface ISocketClient
    {
        Task ConnectAsync();
    }
}
