using System;
using System.Data;
using System.Threading.Tasks;
using Nova.Abstractions.Data.Repositories;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// <see cref="IReportRepository"/> over the V3 data access layer. Same two stored
    /// procedures and the same @Shift/@StartTime/@EndTime parameters
    /// Master.UI's Report_Screen calls, now routed through IDataAccess.
    /// </summary>
    public sealed class LegacyReportRepository : IReportRepository
    {
        private readonly Master.Repository.IDataAccess _dataAccess;

        public LegacyReportRepository(Master.Repository.IDataAccess dataAccess)
        {
            _dataAccess = dataAccess ?? throw new ArgumentNullException(nameof(dataAccess));
        }

        // IDataAccess is synchronous and hits SQL Server, so keep both off the UI thread.
        public Task<DataTable> GetMachineStatusReportAsync(string shift, DateTime startTime, DateTime endTime)
            => Task.Run(() => _dataAccess.GetMachineStatusReport(shift, startTime, endTime));

        public Task<DataTable> GetMachineHealthReportAsync(string shift, DateTime startTime, DateTime endTime)
            => Task.Run(() => _dataAccess.GetMachineHealthReport(shift, startTime, endTime));
    }
}
