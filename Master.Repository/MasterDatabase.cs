using System;
using Nova.Abstractions.Data.Repositories;

namespace Master.Repository
{
    /// <summary>
    /// The one way into TEAL_Master (SQL Server). Builds every repository over the one data
    /// access; everything else uses only the Nova.Abstractions repository contracts.
    /// </summary>
    /// <remarks>
    /// Without a connection string there is no database: every repository is null, and the
    /// pages that read one say their data is not available. Nothing is kept anywhere else
    /// in its place - the earlier CSV fallback could not store anything and is gone.
    /// </remarks>
    public sealed class MasterDatabase
    {
        /// <param name="connectionString">App.config's connectionString; blank when there is no database.</param>
        public MasterDatabase(string connectionString)
        {
            IsAvailable = !string.IsNullOrWhiteSpace(connectionString);
            if (!IsAvailable)
                return;

            IDataAccess dataAccess = new SQLDataAccess(connectionString);

            ShiftSettings = new ShiftSettingsRepository(dataAccess);
            AlarmLog = new AlarmLogRepository(dataAccess);
            Production = new ProductionRepository(dataAccess);
            ProductionRecorder = new ProductionRecorder(dataAccess);
            Reports = new ReportRepository(dataAccess);
            AlarmHistory = new AlarmHistoryRepository(dataAccess);
            DataAccess = dataAccess;
        }

        /// <summary>True when a database is configured; every repository below is null otherwise.</summary>
        public bool IsAvailable { get; }

        public IShiftSettingsRepository ShiftSettings { get; }

        public IAlarmLogRepository AlarmLog { get; }

        public IProductionRepository Production { get; }

        /// <summary>Saves each finished unit into [ProductionData].</summary>
        public IProductionRecorder ProductionRecorder { get; }

        /// <summary>Machine Status / Machine Health report tabs (SP_GetMachineStatusReport / SP_GetMachineHealthReport).</summary>
        public IReportRepository Reports { get; }

        /// <summary>Where Nova.Alarm saves the PLC alarms.</summary>
        public IAlarmHistoryRepository AlarmHistory { get; }

        /// <summary>
        /// The Home page's State Changes chart from [HealthData]; null without a database.
        /// </summary>
        /// <param name="openSegment">The run / idle / down segment not yet written, ending now (ShiftStatusHelper.OpenHealthSegment).</param>
        public Nova.Abstractions.Services.IHiveStateHistory CreateStateHistory(Func<HealthData> openSegment)
        {
            return IsAvailable ? new HealthStateHistory(DataAccess, openSegment) : null;
        }

        /// <summary>
        /// The data access itself, for the code taken over from V3 that works on it directly:
        /// Master.Scheduler (purge, shift status, OEE) through GlobalClass.DataAccess.
        /// Nothing else uses it; the HMI uses the repositories above.
        /// </summary>
        public IDataAccess DataAccess { get; }
    }
}
