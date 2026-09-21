using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nova.Abstractions.Data.Repositories;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// <see cref="IProductionRepository"/> backed by the V3 data access layer. The
    /// bucket loop follows HomeScreen.LoadUPHXYaxisRange(): walk the current shift in
    /// one-hour slots from its start, truncate the last slot, and count each slot
    /// through IDataAccess.GetProductionCountByHour — the same [ProductionData] rows
    /// V3 plots. The shift is read through IDataAccess.GetCurrentShift() on each call.
    /// </summary>
    public sealed class LegacyProductionRepository : IProductionRepository
    {
        private readonly Master.Repository.IDataAccess _dataAccess;

        public LegacyProductionRepository(Master.Repository.IDataAccess dataAccess)
        {
            _dataAccess = dataAccess ?? throw new ArgumentNullException(nameof(dataAccess));
        }

        /// <summary>
        /// Reports → Production Data. V3's Report_Screen never implemented this tab
        /// (btnLoadPrdata_Click ends at "Need to Bind Datasource"), so there is no
        /// stored procedure to reuse; this reads the same [ProductionData] rows through
        /// the existing IDataAccess.GetProductionData(start, end) range query, which is
        /// what ShiftStatusHelper's OEE maths already runs on.
        /// </summary>
        public Task<IReadOnlyList<ProductionUnitRecord>> GetProductionRecordsAsync(
            DateTime start, DateTime end, string shift = null)
        {
            return Task.Run<IReadOnlyList<ProductionUnitRecord>>(() =>
            {
                var rows = _dataAccess.GetProductionData(start, end)
                           ?? new List<Master.Repository.ProductionData>();

                IEnumerable<Master.Repository.ProductionData> query = rows;

                // "ALL" is the no-filter sentinel the report filter uses, matching
                // cmbShiftPrdata's "All" entry in V3.
                if (!string.IsNullOrWhiteSpace(shift) &&
                    !shift.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(r =>
                        string.Equals(r.Shift, shift, StringComparison.OrdinalIgnoreCase));
                }

                return query
                    .OrderByDescending(r => r.ProductionTime)
                    .Select(r => new ProductionUnitRecord
                    {
                        Date = r.Date,
                        Shift = r.Shift,
                        ProductionTime = r.ProductionTime,
                        ProductCode = r.ProductCode,
                        IsPass = r.ProductionStatus,
                        CycleTimeSeconds = r.CycleTime,
                        Iteration = r.Iteration,
                    })
                    .ToList();
            });
        }

        public Task<ShiftProduction> GetShiftHourlyProductionAsync()
        {
            // IDataAccess is synchronous and issues one SELECT COUNT(*) per slot, so
            // keep the whole walk off the UI thread.
            return Task.Run(() =>
            {
                Master.Repository.Shift shift;
                try
                {
                    shift = _dataAccess.GetCurrentShift();
                }
                catch (Exception)
                {
                    // CSV mode has no shift master.
                    shift = null;
                }

                // A null shift, or a zero-length window, means there is no real shift to
                // scope to. The zero-length case is the "Default" placeholder installed
                // at startup when the shift master could not be read: its StartTime and
                // EndTime are both 00:00, and treating that as a window would plot from
                // midnight to now — a whole day instead of one shift.
                if (shift == null || shift.StartTime == shift.EndTime)
                    return new ShiftProduction { HasShift = false };

                DateTime now = DateTime.Now;
                DateTime windowStart = DateTime.Today.Add(shift.StartTime);
                DateTime windowEnd = DateTime.Today.Add(shift.EndTime);

                if (shift.StartTime > shift.EndTime)
                {
                    // Overnight shift (e.g. 22:00-06:00). Before the end time we are in
                    // the run that began yesterday; after the start time it ends tomorrow.
                    if (now.TimeOfDay < shift.EndTime)
                        windowStart = windowStart.AddDays(-1);
                    else
                        windowEnd = windowEnd.AddDays(1);
                }

                // Never plot past the shift's own end, even if CurrentShift is briefly
                // stale between ShiftMonitor ticks — otherwise the next shift's hours
                // would be drawn as if they belonged to this one.
                DateTime plotEnd = now < windowEnd ? now : windowEnd;

                var buckets = new List<HourlyProductionBucket>();
                DateTime slotStart = windowStart;
                while (slotStart < plotEnd)
                {
                    DateTime slotEnd = slotStart.AddHours(1);
                    if (slotEnd > plotEnd)
                        slotEnd = plotEnd;

                    buckets.Add(new HourlyProductionBucket
                    {
                        Start = slotStart,
                        End = slotEnd,
                        Label = $"{slotStart:HH:mm}-{slotEnd:HH:mm}",
                        // Same three per-slot counts HomeScreen.LoadMachineStatus()
                        // reads for its hourly Pass / Fail / UPH series.
                        Count = _dataAccess.GetProductionCountByHour(slotStart, slotEnd),
                        PassCount = _dataAccess.GetPassCountByHour(slotStart, slotEnd),
                        FailCount = _dataAccess.GetFailCountByHour(slotStart, slotEnd),
                    });

                    slotStart = slotEnd;
                }

                return new ShiftProduction
                {
                    HasShift = true,
                    ShiftName = shift.ShiftName,
                    WindowStart = windowStart,
                    WindowEnd = plotEnd,
                    Buckets = buckets,
                };
            });
        }
    }
}
