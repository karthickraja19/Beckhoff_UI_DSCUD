using System;
using System.Collections.Generic;
using System.Data;

namespace Master.Repository
{
    public interface IDataAccess
    {
        Shift GetCurrentShift();
        List<Shift> GetShiftMasterData();
        void UpdateShiftMasterData(string shiftName, DateTime startTime, DateTime endTime);
        void AddHealthData(HealthData healthRecord);
        List<HealthData> GetHealthData(DateTime date, string shift);
        void AddProductionData(ProductionData productionData);
        List<ProductionData> GetProductionData(DateTime StartTime, DateTime EndTime);
        ProductionData GetProductionData(String productCode);
        void UpdateProductionData(String productCode, bool productionStatus, int iteration);
        void AddShiftData(ShiftData shiftData);
        void UpdateShiftPlannedDowntime(DateTime date, string shift, int plannedDownTime, int breakTime);
        void UpdateShiftData(ShiftData shiftData);
        ShiftData GetShiftData(DateTime date, string shift);
        void AddOEEData(OEEData oEEData);
        List<OEEData> GetOEEData(DateTime StartTime, DateTime endTime);
        void AddProductionDataHourly(ProductionDataHourly productionDataHourly);
        int GetProductionCountByHour(DateTime startTime, DateTime endTime);
        List<AlarmData> GetAlarms(DateTime startTime, DateTime endTime);
        void DeleteHealthDataBefore(DateTime retentionDate);
        void DeleteProductionDataBefore(DateTime retentionDate);
        void DeleteShiftStatusDataBefore(DateTime retentionDate);
        void DeleteAlarmDataBefore(DateTime retentionDate);
        void DeleteOEEDataBefore(DateTime retentionDate);
        void InsertAlarmQuery(string _shift, string _smsg, string _command, DateTime Starttime);

        /// <summary>
        /// Saves one alarm and returns its new row ID. <paramref name="endTime"/> is null
        /// while the alarm is active (the Alarm page shows that as "Ongoing").
        /// </summary>
        int InsertAlarm(string shift, string alarmType, string alarmCode, string alarmDescription, DateTime startTime, DateTime? endTime);

        /// <summary>Fills in the EndTime of an alarm's row.</summary>
        void UpdateAlarmEndTime(int alarmId, DateTime endTime);

        /// <summary>Alarms that have no EndTime: still active when the HMI last closed.</summary>
        List<OpenAlarm> GetOpenAlarms();
        System.Data.DataTable AlarmviewQuery(string _shift, DateTime Starttime, DateTime Endtime);

        /// <summary>Reports -> Machine Status, via SP_GetMachineStatusReport.</summary>
        System.Data.DataTable GetMachineStatusReport(string shift, DateTime startTime, DateTime endTime);

        /// <summary>Reports -> Machine Health, via SP_GetMachineHealthReport.</summary>
        System.Data.DataTable GetMachineHealthReport(string shift, DateTime startTime, DateTime endTime);

        int GetPassCountByHour(DateTime startTime, DateTime endTime);
        int GetFailCountByHour(DateTime startTime, DateTime endTime);
        DataTable GetProdData(string _shift, DateTime startTime, DateTime endTime, string barCode);

        DataTable GetMachineHealth(string _shift, DateTime startTime, DateTime endTime, string Category);

        DataTable GetMachineStatus(string _shift, DateTime startTime, DateTime endTime);

        DataTable GetMachineProdData(string _shift, DateTime startTime, DateTime endTime, string ProductCode);
    }
}
