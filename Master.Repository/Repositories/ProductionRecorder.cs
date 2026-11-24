using System;
using System.Threading.Tasks;
using Nova.Abstractions.Data.Repositories;

namespace Master.Repository
{
    /// <summary>
    /// <see cref="IProductionRecorder"/> over [ProductionData]: one row per finished unit,
    /// the rows <see cref="ProductionRepository"/> reads back for the Home and Data pages.
    /// </summary>
    public sealed class ProductionRecorder : IProductionRecorder
    {
        private readonly IDataAccess _dataAccess;

        public ProductionRecorder(IDataAccess dataAccess)
        {
            _dataAccess = dataAccess ?? throw new ArgumentNullException(nameof(dataAccess));
        }

        public Task RecordAsync(ProductionUnitRecord unit)
        {
            if (unit == null)
                throw new ArgumentNullException(nameof(unit));

            return Task.Run(() =>
            {
                string shift = unit.Shift;
                DateTime date = unit.Date;
                if (string.IsNullOrWhiteSpace(shift) || date == default(DateTime))
                {
                    ShiftAt(unit.ProductionTime, out shift, out date);
                    if (!string.IsNullOrWhiteSpace(unit.Shift))
                        shift = unit.Shift;
                    if (unit.Date != default(DateTime))
                        date = unit.Date;
                }

                _dataAccess.AddProductionData(new ProductionData
                {
                    Date = date,
                    Shift = shift ?? string.Empty,
                    ProductionTime = unit.ProductionTime,
                    ProductCode = unit.ProductCode ?? string.Empty,
                    ProductionStatus = unit.IsPass,
                    CycleTime = unit.CycleTimeSeconds,
                    Iteration = unit.Iteration > 0 ? unit.Iteration : 1,
                    Tossing = unit.TossingCount > 0,
                });
            });
        }

        /// <summary>
        /// The shift the unit started in, and its production date: a night-shift unit after
        /// midnight keeps the date its shift started on. No shift master: no shift name.
        /// </summary>
        private void ShiftAt(DateTime time, out string shiftName, out DateTime date)
        {
            shiftName = string.Empty;
            date = time.Date;

            Shift shift;
            try
            {
                shift = _dataAccess.GetCurrentShift();
            }
            catch (Exception)
            {
                return;
            }

            if (shift == null || shift.StartTime == shift.EndTime)
                return;

            shiftName = shift.ShiftName ?? string.Empty;
            if (shift.StartTime > shift.EndTime && time.TimeOfDay < shift.EndTime)
                date = time.Date.AddDays(-1);
        }
    }
}
