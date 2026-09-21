using System.Threading.Tasks;

namespace Nova.Abstractions.Demo
{
    public interface ISampleTestAxis
    {
        string AxisName { get; }
        Task<bool> MoveAbsoluteAsync(double position, double speed);
        Task<bool> HomeAsync();
    }
}
