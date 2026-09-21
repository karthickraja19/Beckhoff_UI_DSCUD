using System.Threading.Tasks;

namespace Nova.Abstractions.Demo
{
    public interface ITestLogger
    {
        string LoggerType { get; }
        event System.Action<string> OnLogWritten;
        Task LogMessageAsync(string message);
    }
}
