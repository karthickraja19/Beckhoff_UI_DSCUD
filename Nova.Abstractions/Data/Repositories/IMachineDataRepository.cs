using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nova.Abstractions.Data.Models;

namespace Nova.Abstractions.Data.Repositories
{
    public interface IMachineDataRepository
    {
        // Shift Master Data
        Task<Shift> GetCurrentShiftAsync();
        Task<IEnumerable<Shift>> GetShiftMasterDataAsync();
        Task UpdateShiftMasterDataAsync(string shiftName, TimeSpan startTime, TimeSpan endTime);

        // Health Data
        Task AddHealthDataAsync(HealthData healthRecord);
        Task<IEnumerable<HealthData>> GetHealthDataAsync(DateTime date, string shift);
        Task DeleteHealthDataBeforeAsync(DateTime retentionDate);

        // Production Data
        Task AddProductionDataAsync(ProductionData productionData);
        Task<ProductionData> GetProductionDataAsync(string productCode);
        Task<IEnumerable<ProductionData>> GetProductionDataAsync(DateTime startTime, DateTime endTime);
        Task UpdateProductionDataAsync(string productCode, bool productionStatus, int iteration);
        Task DeleteProductionDataBeforeAsync(DateTime retentionDate);

        // Hourly Production
        Task AddProductionDataHourlyAsync(ProductionDataHourly productionDataHourly);
        Task<IEnumerable<ProductionDataHourly>> GetProductionDataHourlyAsync(DateTime date, string shift);
        Task<int> GetProductionCountByHourAsync(DateTime startTime, DateTime endTime);

        // Shift Status Data
        Task AddShiftDataAsync(ShiftData shiftData);
        Task UpdateShiftPlannedDowntimeAsync(DateTime date, string shift, int plannedDownTime, int breakTime);
        Task UpdateShiftDataAsync(ShiftData shiftData);
        Task<ShiftData> GetShiftDataAsync(DateTime date, string shift);
        Task DeleteShiftStatusDataBeforeAsync(DateTime retentionDate);

        // OEE Data
        Task AddOEEDataAsync(OEEData oeeData);
        Task<IEnumerable<OEEData>> GetOEEDataAsync(DateTime startTime, DateTime endTime);
        Task DeleteOEEDataBeforeAsync(DateTime retentionDate);

        // Alarm Data
        Task AddAlarmDataAsync(AlarmData alarmData); // Added this to support Alarm insertion
        Task<IEnumerable<AlarmData>> GetAlarmsAsync(DateTime startTime, DateTime endTime);
        Task DeleteAlarmDataBeforeAsync(DateTime retentionDate);
    }
}
