using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nova.Abstractions.Data;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Data.Repositories;

namespace Nova.Core.Services
{
    /// <summary>
    /// The Alarm page's generated alarm log (App.config <c>SampleData=true</c>): one alarm
    /// for each of <see cref="SampleProductionRepository"/>'s alarm stops, with the DFM's
    /// error types (Audio SW DFM v5.4 slides 32-33).
    /// </summary>
    public sealed class SampleAlarmLogRepository : IAlarmLogRepository, ISampleDataSource
    {
        public Task AppendAsync(AlarmData alarm)
        {
            // Rows are written by the PLC's alarm list, never by a page.
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AlarmData>> QueryAsync(DateTime startTime, DateTime endTime, string shift = null)
        {
            return Task.Run<IReadOnlyList<AlarmData>>(() =>
            {
                DateTime now = DateTime.Now;
                var alarms = new List<AlarmData>();

                foreach (SampleStop stop in SampleProductionRepository.Stops(startTime, endTime))
                {
                    if (stop.IsShiftChange || stop.Start < startTime || stop.Start > endTime || stop.Start > now)
                        continue;

                    SampleAlarm alarm = SampleAlarms.For(stop);
                    string alarmShift = SampleProductionRepository.ShiftAtTime(stop.Start);
                    if (!string.IsNullOrWhiteSpace(shift) && !string.Equals(shift, alarmShift, StringComparison.OrdinalIgnoreCase))
                        continue;

                    alarms.Add(new AlarmData
                    {
                        StartTime = stop.Start,
                        // Still standing: no end time yet, as the Alarm page expects.
                        EndTime = stop.End <= now ? stop.End : default(DateTime),
                        AlarmCode = alarm.Code,
                        AlarmType = alarm.Type,
                        AlarmDescription = alarm.Detail,
                        Shift = alarmShift
                    });
                }

                return alarms;
            });
        }
    }

    internal sealed class SampleAlarm
    {
        public SampleAlarm(string code, string type, string detail)
        {
            Code = code;
            Type = type;
            Detail = detail;
        }

        public string Code { get; }
        public string Type { get; }
        public string Detail { get; }
    }

    /// <summary>TE010-shaped alarms in the DFM's error types, picked per stop.</summary>
    internal static class SampleAlarms
    {
        private static readonly SampleAlarm[] All =
        {
            new SampleAlarm("M03GAA2-01", "Motion Error", "Station 1 Y axis move timeout"),
            new SampleAlarm("M02GAA3-02", "Motion Error", "Station 2 dispensing Z axis not in position"),
            new SampleAlarm("C01CVCY-01", "Cylinder Error", "UC loading lifter did not reach the down position"),
            new SampleAlarm("C02CVCY-03", "Cylinder Error", "Transfer conveyor stopper did not reach the up position"),
            new SampleAlarm("S02CVSO-01", "Sensor Error", "UC in-place sensor not detected at the scanning position"),
            new SampleAlarm("V01VSOO-01", "Vision Error", "CCD1 pre-check unable to find pattern"),
            new SampleAlarm("V01VSOO-02", "Vision Error", "CCD2 post-check unable to find pattern"),
            new SampleAlarm("T01CVSS-01", "Scanning Error", "Transfer conveyor barcode scan failed"),
            new SampleAlarm("G01DSOO-01", "Glue Error", "Automatic glue weight out of spec"),
            new SampleAlarm("F02SCOO-01", "Safety Error", "Front door opened during auto run"),
            new SampleAlarm("X01IPOO-01", "Traceability Error", "No answer from MES for the carrier SN"),
        };

        public static SampleAlarm For(SampleStop stop)
        {
            int index = (int)(SampleProductionRepository.Noise(stop.Key, 13) * All.Length) % All.Length;
            return All[index];
        }
    }
}
