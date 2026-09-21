using System.Threading.Tasks;
using Nova.Abstractions.Hardware.Interfaces;

namespace Nova.Core.Comm
{
    /// <summary>
    /// Development stand-in for a TCP device: connect always succeeds and each send
    /// returns a canned ACK, so the console tabs work on a dev PC with no network.
    /// </summary>
    public sealed class SimulatedNetworkProtocol : INetworkProtocol
    {
        private bool _connected;

        public string Name { get; }
        public bool IsConnected => _connected;

        public SimulatedNetworkProtocol(string name)
        {
            Name = name;
        }

        public bool Connect() { _connected = true; return true; }
        public Task<bool> ConnectAsync() { _connected = true; return Task.FromResult(true); }
        public void Disconnect() { _connected = false; }

        public bool Send(string message) => _connected;
        public Task<bool> SendAsync(string message) => Task.FromResult(_connected);

        public string Receive(int bufferSize = 1024) => "ACK (simulated)";
        public Task<string> ReceiveAsync(int bufferSize = 1024) => Task.FromResult("ACK (simulated)");

        public Task<string> SendAndReceiveAsync(string message, int timeoutMs = 4000, int pollIntervalMs = 100)
            => Task.FromResult(_connected ? "ACK " + Shorten(message) : string.Empty);

        public void ClearBufferData() { }

        private static string Shorten(string message)
        {
            if (string.IsNullOrEmpty(message)) return "OK";
            message = message.Replace("\r", " ").Replace("\n", " ").Trim();
            return message.Length <= 40 ? message : message.Substring(0, 40) + "…";
        }
    }
}
