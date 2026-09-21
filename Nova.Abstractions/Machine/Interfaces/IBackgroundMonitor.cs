using System.Threading;
using System.Threading.Tasks;

namespace Nova.Abstractions.Machine.Interfaces
{
    public interface IBackgroundMonitor
    {
        string Name { get; }
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }
}
