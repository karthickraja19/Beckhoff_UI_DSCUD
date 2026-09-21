using System;
using System.Threading.Tasks;

namespace Nova.Abstractions.Demo
{
    public interface IDemoSequenceFactory
    {
        Task RunDemoAsync(string axisBrand, string loggerType, Action<string> onLog);
    }
}
