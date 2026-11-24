using Master.Repository;
using Master.SharedUtilities;
using Quartz;
using Quartz.Impl;
using System;
using System.Configuration;
using System.Threading.Tasks;

namespace Master.Scheduler
{
    /// <summary>
    /// The HMI's scheduled housekeeping (Quartz):
    /// - every 2 min, the CSV files under App.config LogFilePath older than RetentionDaysForCSV;
    /// - every StatusRefreshSeconds, the current shift's status, OEE and the shift change;
    /// - daily at 23:30, the alarm, health, production and shift status rows past their
    ///   RetentionDaysFor* and the OEE rows from before today.
    /// The database jobs do nothing without a database (no data access or no current shift).
    /// </summary>
    public static class ScheduleJobs
    {
        private const string DailyPurgeCron = "0 30 23 ? * *";

        private static IScheduler _scheduler;

        /// <summary>
        /// Hands the V3 helpers their data access and current shift, then starts the jobs.
        /// </summary>
        /// <param name="dataAccess">TEAL_Master; null when there is no database.</param>
        public static async Task Start(IDataAccess dataAccess)
        {
            GlobalClass.DataAccess = dataAccess;
            GlobalClass.CurrentShift = null;
            if (dataAccess != null)
            {
                try
                {
                    GlobalClass.CurrentShift = dataAccess.GetCurrentShift();
                    if (GlobalClass.CurrentShift != null)
                    {
                        ShiftStatusHelper.CreateOrUpdateShiftData();
                        ShiftStatusHelper.GetShiftStatus();
                        ShiftStatusHelper.GetHealthData();
                    }
                    else
                    {
                        Teal.Library.Log.Logger.WriteLog(Teal.Library.Log.LogType.Error, "Scheduler", "Start",
                            "There is no current shift (the shift table is empty, unreadable, or none covers this time), "
                            + "so no run / down / idle time, shift status or OEE is recorded until one is found.", string.Empty);
                    }
                }
                catch (Exception ex)
                {
                    Teal.Library.Log.Logger.WriteLog(Teal.Library.Log.LogType.Error, "Scheduler", "Start",
                        "The current shift could not be read: " + ex.Message, ex.ToString());
                }
            }

            await Schedule().ConfigureAwait(false);
        }

        /// <summary>Stops the jobs; one that is running is let finish.</summary>
        public static void Shutdown()
        {
            IScheduler scheduler = _scheduler;
            _scheduler = null;
            if (scheduler == null)
                return;

            try
            {
                scheduler.Shutdown(true).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Teal.Library.Log.Logger.WriteLog(Teal.Library.Log.LogType.Error, "Scheduler", "Shutdown", ex.Message, ex.ToString());
            }
        }

        public static async Task Schedule()
        {
            if (_scheduler != null)
                return;

            ISchedulerFactory schedulerFactory = new StdSchedulerFactory();
            IScheduler scheduler = await schedulerFactory.GetScheduler();
            _scheduler = scheduler;

            IJobDetail deleteFilesJob = JobBuilder.Create<DeleteFilesJob>()
             .WithIdentity("deleteFilesJob", "group1")
             .Build();

            ITrigger deleteFilesTrigger = TriggerBuilder.Create()
              .WithIdentity("deleteFilesTrigger", "group1")
              .StartNow()
              .WithSimpleSchedule(x => x
               .WithIntervalInSeconds(120)
               .RepeatForever())
              .Build();

            IJobDetail shiftStatusRefresherJob = JobBuilder.Create<ShiftStatusRefresherJob>()
             .WithIdentity("shiftStatusRefresherJob", "group1")
             .Build();

            ITrigger shiftStatusRefresherTrigger = TriggerBuilder.Create()
              .WithIdentity("shiftStatusRefresherTrigger", "group1")
              .StartNow()
              .WithSimpleSchedule(x => x
               .WithIntervalInSeconds(int.Parse(ConfigurationManager.AppSettings["StatusRefreshSeconds"]))
               .RepeatForever())
              .Build();

            IJobDetail deleteAlarmDataJob = JobBuilder.Create<DeleteAlarmDataJob>()
             .WithIdentity("deleteAlarmDataJob", "group1")
             .Build();

            IJobDetail deleteHealthDataJob = JobBuilder.Create<DeleteHealthDataJob>()
             .WithIdentity("deleteHealthDataJob", "group1")
             .Build();

            IJobDetail deleteOEEDataJob = JobBuilder.Create<DeleteOEEDataJob>()
             .WithIdentity("deleteOEEDataJob", "group1")
             .Build();

            IJobDetail deleteProductionDataJob = JobBuilder.Create<DeleteProductionDataJob>()
             .WithIdentity("deleteProductionDataJob", "group1")
             .Build();

            IJobDetail deleteShiftStatusDataJob = JobBuilder.Create<DeleteShiftStatusDataJob>()
             .WithIdentity("deleteShiftStatusDataJob", "group1")
             .Build();

            ITrigger CreateDailyTrigger(string name) =>
                TriggerBuilder.Create()
                    .WithIdentity(name, "group1")
                    .WithCronSchedule(DailyPurgeCron)
                    .Build();

            await scheduler.Start();
            await scheduler.ScheduleJob(deleteFilesJob, deleteFilesTrigger);
            await scheduler.ScheduleJob(shiftStatusRefresherJob, shiftStatusRefresherTrigger);
            await scheduler.ScheduleJob(deleteAlarmDataJob, CreateDailyTrigger("deleteAlarmTrigger"));
            await scheduler.ScheduleJob(deleteHealthDataJob, CreateDailyTrigger("deleteHealthTrigger"));
            await scheduler.ScheduleJob(deleteOEEDataJob, CreateDailyTrigger("deleteOEETrigger"));
            await scheduler.ScheduleJob(deleteProductionDataJob, CreateDailyTrigger("deleteProductionTrigger"));
            await scheduler.ScheduleJob(deleteShiftStatusDataJob, CreateDailyTrigger("deleteShiftStatusTrigger"));
        }
    }
}
