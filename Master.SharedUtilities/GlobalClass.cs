using Master.Repository;
using Master.SharedUtilities.Model;
using NLog;

namespace Master.SharedUtilities
{
    /// <summary>
    /// The state ShiftStatusHelper works on, as in V3. Master.Scheduler sets DataAccess and
    /// CurrentShift (ScheduleJobs.Start, ShiftStatusRefresherJob).
    /// </summary>
    public static class GlobalClass
    {
        /// <summary>TEAL_Master; null when there is no database.</summary>
        public static IDataAccess DataAccess;

        private static Shift _currentShift;

        /// <summary>
        /// The shift now running, from the shift master; null when there is none. Setting it also
        /// sets NLog's ShiftName (the shift's name with spaces as '_', "Default" without a shift),
        /// which the log files' names carry (NLog.config, ${gdc:ShiftName}).
        /// </summary>
        public static Shift CurrentShift
        {
            get { return _currentShift; }
            set
            {
                _currentShift = value;
                string name = value == null ? null : value.ShiftName;
                GlobalDiagnosticsContext.Set("ShiftName",
                    string.IsNullOrWhiteSpace(name) ? "Default" : name.Trim().Replace(' ', '_'));
            }
        }

        /// <summary>The current shift's run, down and idle time, read from [HealthData].</summary>
        public static MachineHealth Health = new MachineHealth();

        /// <summary>The current shift's status as last calculated.</summary>
        public static ShiftStatus ShiftStatusData = new ShiftStatus();
    }
}
