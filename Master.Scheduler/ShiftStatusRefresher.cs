using Master.SharedUtilities;
using Quartz;
using System;
using System.Threading.Tasks;

namespace Master.Scheduler
{
    /// <summary>
    /// Every StatusRefreshSeconds: closes the shift that has ended (V3's ShiftMonitor: its
    /// final status and ProductionData.csv), moves to the current one, then recalculates the
    /// current shift's status and OEE. The run, down and idle time are read again each time.
    /// </summary>
    [DisallowConcurrentExecution]
    public class ShiftStatusRefresherJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            if (GlobalClass.DataAccess == null)
                return Task.CompletedTask;

            try
            {
                var shiftNow = GlobalClass.DataAccess.GetCurrentShift();
                if (shiftNow == null)
                    return Task.CompletedTask;

                if (GlobalClass.CurrentShift == null)
                {
                    // A segment opened with no shift running is counted to this one from now.
                    ShiftStatusHelper.SplitHealthSegment(DateTime.Now, shiftNow);
                    GlobalClass.CurrentShift = shiftNow;
                }
                else if (GlobalClass.CurrentShift.ShiftName != shiftNow.ShiftName)
                {
                    // The shift that ended, on the date it started (the day before for a
                    // night shift), counted to its end and not to now.
                    var ended = GlobalClass.CurrentShift;
                    DateTime endedAt = ShiftStatusHelper.LastEndOf(ended);
                    DateTime endedDate = ShiftStatusHelper.ShiftDateOf(endedAt.AddSeconds(-1), ended);

                    // The run / down / idle segment under way is counted to each shift up to
                    // its boundary, so the shift ending gets its part before its final status.
                    ShiftStatusHelper.SplitHealthSegment(endedAt, shiftNow);
                    ShiftStatusHelper.CalculateShiftStatus(ended, endedDate);
                    ShiftStatusHelper.WriteProductionDataToCsv(ended, endedDate);
                    GlobalClass.CurrentShift = shiftNow;
                }

                ShiftStatusHelper.CreateOrUpdateShiftData();
                ShiftStatusHelper.CalculateCurrentShiftStatus();
            }
            catch (Exception ex)
            {
                Teal.Library.Log.Logger.WriteLog(Teal.Library.Log.LogType.Error, "Scheduler", "ShiftStatusRefresher",
                    "The shift status could not be refreshed: " + ex.Message, ex.ToString());
            }

            return Task.CompletedTask;
        }
    }
}
