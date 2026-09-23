namespace Nova.Alarm
{
    /// <summary>
    /// One entry of the PLC's alarm list, exactly as the PLC reports it
    /// (ST_Alarms in GVL_Alarm.fbAlarmManager.aAlarms).
    /// </summary>
    public sealed class PlcAlarm
    {
        public string StationName { get; set; }

        public string ComponentName { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorType { get; set; }

        public string ErrorDescription { get; set; }

        /// <summary>1 = MODULE (only its module stops), 2 = STATION (the whole machine stops).</summary>
        public short Level { get; set; }

        /// <summary>Module of the component that raised the alarm, 0 = no module.</summary>
        public short ModuleNo { get; set; }

        /// <summary>The PLC marks a free slot with an empty component name and error code.</summary>
        public bool IsEmpty
        {
            get { return ComponentName == "" && ErrorCode == ""; }
        }

        /// <summary>
        /// The text saved as the alarm's detail, e.g. "MACHINE / ESTOP_FRONT : EMERGENCY_PRESSED".
        /// </summary>
        public string ErrorDetail
        {
            get { return StationName + " / " + ComponentName + " : " + ErrorDescription; }
        }
    }
}
