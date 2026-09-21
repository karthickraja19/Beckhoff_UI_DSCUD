using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nova.Abstractions.Data.Repositories;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// <see cref="IShiftSettingsRepository"/> over the V3 data access layer, calling the
    /// same three IDataAccess members Master.UI's Settings form uses:
    /// GetShiftMasterData, UpdateShiftMasterData and UpdateShiftPlannedDowntime.
    ///
    /// V3 followed the downtime writes with a ShiftStatusHelper recompute. That helper
    /// lived in Master.SharedUtilities and comes back with the shift/OEE service.
    /// </summary>
    public sealed class LegacyShiftSettingsRepository : IShiftSettingsRepository
    {
        private readonly Master.Repository.IDataAccess _dataAccess;

        public LegacyShiftSettingsRepository(Master.Repository.IDataAccess dataAccess)
        {
            _dataAccess = dataAccess ?? throw new ArgumentNullException(nameof(dataAccess));
        }

        public Task<IReadOnlyList<ShiftDefinition>> GetShiftsAsync()
        {
            return Task.Run<IReadOnlyList<ShiftDefinition>>(() =>
            {
                try
                {
                    var shifts = _dataAccess.GetShiftMasterData();
                    if (shifts != null && shifts.Count > 0)
                    {
                        return shifts
                            .Select(s => new ShiftDefinition
                            {
                                ShiftName = s.ShiftName,
                                StartTime = s.StartTime,
                                EndTime = s.EndTime,
                            })
                            .ToList();
                    }
                }
                catch (NotImplementedException)
                {
                    // Expected in CSV/Offline mode
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("GetShiftMasterData failed: " + ex.Message);
                }

                // Fallback default shifts for CSV/Offline mode
                return new List<ShiftDefinition>
                {
                    new ShiftDefinition { ShiftName = "Shift A", StartTime = TimeSpan.FromHours(6), EndTime = TimeSpan.FromHours(14) },
                    new ShiftDefinition { ShiftName = "Shift B", StartTime = TimeSpan.FromHours(14), EndTime = TimeSpan.FromHours(22) },
                    new ShiftDefinition { ShiftName = "Shift C", StartTime = TimeSpan.FromHours(22), EndTime = TimeSpan.FromHours(6) }
                };
            });
        }

        public Task UpdateShiftAsync(string shiftName, TimeSpan startTime, TimeSpan endTime)
        {
            return Task.Run(() =>
            {
                try
                {
                    _dataAccess.UpdateShiftMasterData(
                        shiftName,
                        DateTime.Today.Add(startTime),
                        DateTime.Today.Add(endTime));
                }
                catch (NotImplementedException)
                {
                    // Safe fallback in CSV/Offline mode
                }
            });
        }

        public Task UpdatePlannedDowntimeAsync(DateTime shiftDate, string shiftName, int downtimeMinutes, int breakMinutes)
        {
            return Task.Run(() =>
            {
                try
                {
                    _dataAccess.UpdateShiftPlannedDowntime(shiftDate.Date, shiftName, downtimeMinutes, breakMinutes);
                }
                catch (NotImplementedException)
                {
                    // Safe fallback in CSV/Offline mode
                }
            });
        }

        public Task RecalculateShiftAsync(DateTime shiftDate, string shiftName, int downtimeMinutes, int breakMinutes)
        {
            return Task.Run(() =>
            {
                try
                {
                    _dataAccess.UpdateShiftPlannedDowntime(shiftDate.Date, shiftName, downtimeMinutes, breakMinutes);
                }
                catch (NotImplementedException)
                {
                    // Safe fallback in CSV/Offline mode
                }
            });
        }
    }
}
