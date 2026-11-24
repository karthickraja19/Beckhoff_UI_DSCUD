using System.Collections.Generic;
using Nova.Abstractions.Hardware;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Registry of all steppers (axis-based and IO conveyors), injected into
    /// ViewModels the same way IAxisService is for servos.
    /// </summary>
    public interface IStepperService
    {
        IStepper GetStepper(string componentId);
        IReadOnlyList<IStepper> GetAllSteppers();
    }
}
