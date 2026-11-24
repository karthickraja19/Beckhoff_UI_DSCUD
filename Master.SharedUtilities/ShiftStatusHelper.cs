using Master.Repository;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;

namespace Master.SharedUtilities
{
    public static class ShiftStatusHelper
    {
        /// <summary>The [HealthData] categories.</summary>
        public const string RunTime = "RunTime";
        public const string DownTime = "DownTime";
        public const string IdleTime = "IdleTime";

        private static List<ProductionData> productionDatas;
        private static DateTime shiftStartTime;
        private static DateTime endTimeForCalculation;

        // The health segment still open: what the machine has been doing since _openSince,
        // counted to the shift it started in. Written to [HealthData] when it ends.
        private static readonly object HealthLock = new object();
        private static string _openCategory;
        private static DateTime _openSince;
        private static string _openShiftName;
        private static DateTime _openShiftDate;

        public static void GetHealthData()
        {
            if (GlobalClass.CurrentShift == null)
                return;

            ReadHealth(GetShiftDate(), GlobalClass.CurrentShift.ShiftName);
        }

        /// <summary>
        /// The shift's run, down and idle time into GlobalClass.Health: its [HealthData] rows,
        /// plus the open segment so far when it belongs to that shift.
        /// </summary>
        private static void ReadHealth(DateTime shiftDate, string shiftName)
        {
            lock (HealthLock)
            {
                List<HealthData> healthList = GlobalClass.DataAccess.GetHealthData(shiftDate, shiftName);

                double run = healthList.Where(x => x.HealthCategory == RunTime).Sum(x => x.TotalTime);
                double down = healthList.Where(x => x.HealthCategory == DownTime).Sum(x => x.TotalTime);
                double idle = healthList.Where(x => x.HealthCategory == IdleTime).Sum(x => x.TotalTime);

                if (_openCategory != null && _openShiftName == shiftName && _openShiftDate == shiftDate.Date)
                {
                    double open = Math.Max(0, (DateTime.Now - _openSince).TotalSeconds);
                    if (_openCategory == RunTime) run += open;
                    else if (_openCategory == DownTime) down += open;
                    else if (_openCategory == IdleTime) idle += open;
                }

                GlobalClass.Health.Runtime = run;
                GlobalClass.Health.Downtime = down;
                GlobalClass.Health.Idletime = idle;
            }
        }

        /// <summary>
        /// What the machine does from <paramref name="at"/> on: RunTime, DownTime or IdleTime,
        /// or null when it is not known (PLC not live, HMI closing). The segment before it is
        /// written to [HealthData]; the same category again changes nothing.
        /// </summary>
        public static void SetHealthCategory(string category, DateTime at)
        {
            // A blank category is "not known", never a row of its own.
            if (string.IsNullOrWhiteSpace(category))
                category = null;

            lock (HealthLock)
            {
                if (_openCategory == category)
                    return;

                try
                {
                    CloseOpenSegment(at);
                }
                finally
                {
                    _openCategory = category;
                    _openSince = at;
                    Shift shift = GlobalClass.CurrentShift;
                    _openShiftName = shift?.ShiftName;
                    _openShiftDate = ShiftDateOf(at, shift);
                }
            }
        }

        /// <summary>
        /// The run / down / idle segment under way, from its start to now, as the row it will
        /// be written as; null when nothing is being counted. For the Home page's State Changes
        /// chart, which reads the written rows and needs the part not written yet.
        /// </summary>
        public static HealthData OpenHealthSegment()
        {
            lock (HealthLock)
            {
                if (_openCategory == null)
                    return null;

                DateTime now = DateTime.Now;
                return new HealthData
                {
                    Date = _openShiftDate,
                    Shift = _openShiftName,
                    HealthCategory = _openCategory,
                    StartTime = _openSince,
                    EndTime = now,
                    TotalTime = Math.Max(0, (now - _openSince).TotalSeconds)
                };
            }
        }

        /// <summary>
        /// At a shift change: the open segment is written up to <paramref name="at"/> (the end
        /// of the shift ending) and goes on, from then, in <paramref name="next"/>.
        /// </summary>
        public static void SplitHealthSegment(DateTime at, Shift next)
        {
            lock (HealthLock)
            {
                if (_openCategory == null)
                    return;

                try
                {
                    CloseOpenSegment(at);
                }
                finally
                {
                    if (at > _openSince)
                        _openSince = at;
                    _openShiftName = next?.ShiftName;
                    _openShiftDate = ShiftDateOf(_openSince, next);
                }
            }
        }

        /// <summary>Writes the open segment up to <paramref name="end"/>; whole seconds, nothing under one.</summary>
        private static void CloseOpenSegment(DateTime end)
        {
            if (_openCategory == null || _openShiftName == null || GlobalClass.DataAccess == null)
                return;

            int seconds = (int)Math.Round((end - _openSince).TotalSeconds);
            if (seconds <= 0)
                return;

            GlobalClass.DataAccess.AddHealthData(new HealthData
            {
                Date = _openShiftDate,
                Shift = _openShiftName,
                StartTime = _openSince,
                EndTime = end,
                HealthCategory = _openCategory,
                TotalTime = seconds
            });
        }

        public static void GetShiftStatus()
        {           
            var shiftStatus = GlobalClass.DataAccess.GetShiftData(GetShiftDate(), GlobalClass.CurrentShift.ShiftName);                    
            UpdateShiftStatusInMemory(shiftStatus);
            
        }

        /// <summary>The current shift's status so far.</summary>
        public static void CalculateCurrentShiftStatus()
        {
           CalculateAndUpdateShitStatus(GlobalClass.CurrentShift, GetShiftDate());
        }

        /// <summary>The status of <paramref name="selectedShift"/> started on <paramref name="shiftDate"/>; a shift that has ended is counted to its end.</summary>
        public static void CalculateShiftStatus(Shift selectedShift, DateTime shiftDate)
        {
            CalculateAndUpdateShitStatus(selectedShift, shiftDate);
        }

        private static void CalculateAndUpdateShitStatus(Shift selectedShift, DateTime shiftDate)
        {
            var shiftStatus = GlobalClass.DataAccess.GetShiftData(shiftDate, selectedShift.ShiftName);

            // The run, down and idle time as they are now, not as they were at startup.
            ReadHealth(shiftDate, selectedShift.ShiftName);

            // The shift from its start (on the date it started) to now, or to its end once it has ended.
            DateTime shiftEndTime;
            ShiftWindow(selectedShift, shiftDate, out shiftStartTime, out shiftEndTime);
            endTimeForCalculation = DateTime.Now < shiftEndTime ? DateTime.Now : shiftEndTime;

            productionDatas = GlobalClass.DataAccess.GetProductionData(shiftStartTime, endTimeForCalculation);

            OEEData oEEData = new OEEData();

            var availableTime = AvailableSeconds(shiftStartTime, shiftEndTime, endTimeForCalculation,
                shiftStatus?.PlannedDownTime ?? 0, shiftStatus?.BreakTime ?? 0);

            oEEData.Availability = StatusDataCalculator.GetAvailability(GlobalClass.Health.Runtime, availableTime);

            oEEData.Performance = StatusDataCalculator.GetPerformace(GetUnitsProduced(),
                GlobalClass.Health.Runtime, double.Parse(ConfigurationManager.AppSettings["IdealCycleTime"]));
            oEEData.Quality = StatusDataCalculator.GetQuality(GetUnitsPassedinFirstAttempt(), GetUnitsAttempted());
            oEEData.DateTime = endTimeForCalculation;

            ShiftData shiftData = new ShiftData();
            shiftData.OEE = StatusDataCalculator.GetOEE(oEEData.Availability, oEEData.Performance, oEEData.Quality);
            shiftData.Shift = selectedShift.ShiftName;
            shiftData.Date = shiftDate;
            shiftData.RunTime = GlobalClass.Health.Runtime;
            shiftData.DownTime = GlobalClass.Health.Downtime;
            shiftData.IdleTime = GlobalClass.Health.Idletime;
            shiftData.PassCount = GetUnitsSucceeded();
            shiftData.FailCount = GetUnitsFailed();
            shiftData.Yield = StatusDataCalculator.GetYield(shiftData.PassCount, (shiftData.PassCount + shiftData.FailCount));
            shiftData.RetestRate = StatusDataCalculator.GetRetestRate(GetUnitsRetested(), GetUnitsAttempted());
            shiftData.TossingRate = StatusDataCalculator.GetTossingRate(shiftData.FailCount, GetUnitsAttempted());
            shiftData.MTBF = StatusDataCalculator.GetMTBF(GlobalClass.Health.Runtime, GetAlarmsCount(shiftStartTime, endTimeForCalculation));
            shiftData.MTBA = StatusDataCalculator.GetMTBA(GlobalClass.Health.Runtime, GetStopCounts(shiftDate, selectedShift.ShiftName));
            shiftData.LastUpdated = DateTime.Now;
            GlobalClass.DataAccess.AddOEEData(oEEData);
            GlobalClass.DataAccess.UpdateShiftData(shiftData);

            UpdateShiftStatusInMemory(shiftData);
        }

        /// <summary>
        /// Where <paramref name="shift"/> started on <paramref name="shiftDate"/> begins and
        /// ends; a shift past midnight ends the next day.
        /// </summary>
        public static void ShiftWindow(Shift shift, DateTime shiftDate, out DateTime start, out DateTime end)
        {
            start = shiftDate.Date + shift.StartTime;
            end = shiftDate.Date + shift.EndTime;
            if (end <= start)
                end = end.AddDays(1);
        }

        /// <summary>
        /// The seconds the machine was planned to produce from the shift start to
        /// <paramref name="until"/>: the time elapsed less the shift's planned downtime and
        /// break (minutes, for the whole shift), taken in proportion to the part of the shift
        /// elapsed - so all of it once the shift has ended. Never below 0.
        /// </summary>
        private static double AvailableSeconds(DateTime start, DateTime end, DateTime until,
            double plannedDowntimeMinutes, double breakMinutes)
        {
            double shiftSeconds = (end - start).TotalSeconds;
            double elapsed = Math.Max(0, (until - start).TotalSeconds);
            if (shiftSeconds <= 0)
                return 0;

            double planned = Math.Max(0, plannedDowntimeMinutes + breakMinutes) * 60;
            double available = elapsed - planned * Math.Min(1, elapsed / shiftSeconds);
            return Math.Max(0, available);
        }

        private static int GetStopCounts(DateTime shiftDate, string shiftName)
        {
            return GlobalClass.DataAccess.GetHealthData(shiftDate, shiftName)
                .Where(x => x.HealthCategory == DownTime).Count();
        }

        private static int GetAlarmsCount(DateTime shiftStartTime, DateTime endTimeForCalculation)
        {
            return GlobalClass.DataAccess.GetAlarms(shiftStartTime, endTimeForCalculation).Count();
        }

        private static int GetUnitsRetested()
        {
            return productionDatas.Where(x => x.ProductionStatus == true && x.Iteration > 1).Count();
        }

        private static int GetUnitsFailed()
        {
            return productionDatas.Where(x => x.ProductionStatus == false).Count();
        }

        private static int GetUnitsSucceeded()
        {
            return productionDatas.Where(x => x.ProductionStatus == true).Count();
        }

        private static int GetUnitsAttempted()
        {
            return productionDatas.Count();
        }

        private static int GetUnitsPassedinFirstAttempt()
        {
            return productionDatas.Where(x => x.ProductionStatus == true && x.Iteration == 1).Count();
        }

        private static int GetUnitsProduced()
        {
            return productionDatas.Where(x => x.ProductionStatus == true).Count();
        }

        public static void AddHourlyProductionData(DateTime startTime, DateTime endTime)
        {
            GlobalClass.DataAccess.AddProductionDataHourly(new ProductionDataHourly
            {
                Date = GetShiftDate(),
                Shift = GlobalClass.CurrentShift.ShiftName,
                Hour = endTime.Hour,
                ProductCount = GlobalClass.DataAccess.GetProductionCountByHour(startTime, endTime)
            });
        }

        public static void AddHealthData(String healthCategory, DateTime startTime, DateTime endTime, int totalTime)
        {
           GlobalClass.DataAccess.AddHealthData(new HealthData
            {
                Date = GetShiftDate(),
                Shift = GlobalClass.CurrentShift.ShiftName,
                StartTime = startTime,
                EndTime = endTime,
                HealthCategory = healthCategory,
                TotalTime = totalTime
            });
        }

        public static void CreateOrUpdateShiftData()
        {
            var shiftDate = GetShiftDate();

            var shiftData = GlobalClass.DataAccess.GetShiftData(shiftDate, GlobalClass.CurrentShift.ShiftName);

            if (shiftData == null)
            {
                GlobalClass.DataAccess.AddShiftData(new ShiftData
                {
                    Date = shiftDate,
                    Shift = GlobalClass.CurrentShift.ShiftName
                });
            }
        }

        public static DateTime GetShiftDate()
        {
            return ShiftDateOf(DateTime.Now, GlobalClass.CurrentShift);
        }

        /// <summary>
        /// The date <paramref name="shift"/> started on, for a time within it: the day before
        /// for the part of a shift past midnight. Compared to the minute, not the hour, so a
        /// shift ending at 06:30 is still the day before at 06:15.
        /// </summary>
        public static DateTime ShiftDateOf(DateTime at, Shift shift)
        {
            if (shift != null && shift.StartTime > shift.EndTime && at.TimeOfDay < shift.EndTime)
                return at.Date.AddDays(-1);

            return at.Date;
        }

        /// <summary>When <paramref name="shift"/> last ended, at or before now.</summary>
        public static DateTime LastEndOf(Shift shift)
        {
            DateTime end = DateTime.Now.Date + shift.EndTime;
            return end > DateTime.Now ? end.AddDays(-1) : end;
        }

        private static void UpdateShiftStatusInMemory(ShiftData shiftStatus)
        {
            GlobalClass.ShiftStatusData.PassCount = shiftStatus.PassCount;
            GlobalClass.ShiftStatusData.FailCount = shiftStatus.FailCount;
            GlobalClass.ShiftStatusData.Yield = shiftStatus.Yield;
            GlobalClass.ShiftStatusData.OEE = shiftStatus.OEE;
            GlobalClass.ShiftStatusData.MTBF = shiftStatus.MTBF;
            GlobalClass.ShiftStatusData.MTBA = shiftStatus.MTBA;
            GlobalClass.ShiftStatusData.TossingRate = shiftStatus.TossingRate;
            GlobalClass.ShiftStatusData.RetestRate = shiftStatus.RetestRate;
            GlobalClass.ShiftStatusData.UPH = GlobalClass.DataAccess.GetProductionCountByHour(DateTime.Now.AddHours(-1), DateTime.Now);
            GlobalClass.ShiftStatusData.LastUpdated = shiftStatus.LastUpdated;
        }

        /// <summary>Appends the production rows of <paramref name="shift"/> started on <paramref name="shiftDate"/> to ProductionData.csv.</summary>
        public static void WriteProductionDataToCsv(Shift shift, DateTime shiftDate)
        {
            DateTime shiftStartDateTime, shiftEndDateTime;
            ShiftWindow(shift, shiftDate, out shiftStartDateTime, out shiftEndDateTime);
            List<ProductionData> productionDataList =
                GlobalClass.DataAccess.GetProductionData(shiftStartDateTime, shiftEndDateTime);

            string logfilepath = ConfigurationManager.AppSettings["LogFilePath"] + @"\ProductionData.csv";
            var sb = new StringBuilder();

            if (!File.Exists(logfilepath))
            {
                sb.AppendLine("Date,Shift,ProductionTime,ProductCode,ProductionStatus,CycleTime,Iteration");
            }

            foreach (var productionData in productionDataList)
            {
                sb.AppendLine(string.Join(",",
                    productionData.Date,
                    productionData.Shift,
                    productionData.ProductionTime,
                    productionData.ProductCode,
                    productionData.ProductionStatus,
                    productionData.CycleTime,
                    productionData.Iteration
                ));
            }

            File.AppendAllText(logfilepath, sb.ToString());
        }

    }
}
