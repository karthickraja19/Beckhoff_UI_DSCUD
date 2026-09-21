using System.Collections.Generic;
using Nova.Abstractions.Hardware;

namespace Nova.Abstractions.Services
{
    public interface ICylinderService
    {
        IEnumerable<ICylinder> GetAllCylinders();
        ICylinder GetCylinder(string componentId);
    }
}
