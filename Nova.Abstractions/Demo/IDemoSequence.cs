using System;
using System.Threading.Tasks;

namespace Nova.Abstractions.Demo
{
    public interface IDemoSequence
    {
        event Action<string> LogOutput;
        Task ExecuteAsync();
    }
}
