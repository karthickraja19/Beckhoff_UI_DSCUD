using System;
using System.Data;
using System.Threading.Tasks;

namespace Nova.Abstractions.Data.Repositories
{
    /// <summary>
    /// The two stored-procedure reports behind Reports → Production. Both return
    /// whatever columns their procedure defines, so the result stays a DataTable and
    /// the grid generates its columns from it — the same thing V3's Report_Screen does
    /// with dgvMachineStatus/dgvMachineHealth.DataSource = dt.DefaultView.
    /// </summary>
    public interface IReportRepository
    {
        Task<DataTable> GetMachineStatusReportAsync(string shift, DateTime startTime, DateTime endTime);

        Task<DataTable> GetMachineHealthReportAsync(string shift, DateTime startTime, DateTime endTime);
    }
}
