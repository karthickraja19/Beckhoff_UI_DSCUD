using System.Threading.Tasks;
using Nova.Abstractions.Services;

namespace Nova.Core.Hardware
{
    /// <summary>Development stand-in for an OPT lighting controller: connect + apply always succeed.</summary>
    public sealed class SimulatedLightController : ILightController
    {
        public string Name { get; }
        public bool IsConnected { get; private set; }

        public SimulatedLightController(string name)
        {
            Name = name;
        }

        public Task<bool> ConnectAsync() { IsConnected = true; return Task.FromResult(true); }
        public Task<bool> ApplyAsync(string channelValues) => Task.FromResult(IsConnected);
        public void Disconnect() { IsConnected = false; }
    }
}
