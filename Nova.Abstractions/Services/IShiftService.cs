using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nova.Abstractions.Data.Models;

namespace Nova.Abstractions.Services
{
    public interface IShiftService
    {
        Task GetHealthDataAsync();
        Task GetShiftStatusAsync();
        Task CalculateCurrentShiftStatusAsync(bool includePlannedDowntime = false);
        Task CalculateShiftStatusAsync(Shift selectedShift, DateTime shiftDate);
        Task AddHourlyProductionDataAsync(DateTime startTime, DateTime endTime);
        Task AddHealthDataAsync(string healthCategory, DateTime startTime, DateTime endTime, int totalTime);
        Task CreateOrUpdateShiftDataAsync();
        Task WriteProductionDataToCsvAsync(TimeSpan shiftStartTime, TimeSpan shiftEndTime);
        
        Task<DateTime> GetShiftDateAsync();
    }
}
