using System.Collections.Generic;
using Nova.Abstractions.Hardware;

namespace Nova.Abstractions.Services
{
    public interface IVacuumService
    {
        IEnumerable<IVacuum> GetAllVacuums();
        IVacuum GetVacuum(string componentId);
    }
}
