using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware.Interfaces
{
    public interface INetworkProtocol
    {
        bool IsConnected { get; }
        string Name { get; }
        
        bool Connect();
        Task<bool> ConnectAsync();
        
        void Disconnect();
        
        bool Send(string message);
        Task<bool> SendAsync(string message);
        
        string Receive(int bufferSize = 1024);
        Task<string> ReceiveAsync(int bufferSize = 1024);
        
        Task<string> SendAndReceiveAsync(string message, int timeoutMs = 4000, int pollIntervalMs = 100);
        void ClearBufferData();
    }
}
