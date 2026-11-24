using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nova.Abstractions.Services;

namespace Master.Repository
{
    /// <summary>
    /// The Home page's State Changes chart from [HealthData]: the run / idle / down segments
    /// MachineHealthRecorder writes from the PLC's E_MachineState, read back as state changes
    /// (RunTime = 1 running, IdleTime = 2 idle, DownTime = 5 downtime). A time no segment
    /// covers (HMI closed, PLC not live) is state 0, which the chart shows as "No record".
    /// </summary>
    /// <remarks>
    /// The segment under way is not in the table until it ends, so it comes from
    /// <c>openSegment</c> (ShiftStatusHelper.OpenHealthSegment), up to now - today's column
    /// would otherwise stop at the last state change.
    /// </remarks>
    public sealed class HealthStateHistory : IHiveStateHistory
    {
        private const int NoRecord = 0;

        private readonly IDataAccess _dataAccess;
        private readonly Func<HealthData> _openSegment;

        /// <param name="openSegment">The segment not yet written, ending now; null when there is none.</param>
        public HealthStateHistory(IDataAccess dataAccess, Func<HealthData> openSegment)
        {
            _dataAccess = dataAccess ?? throw new ArgumentNullException(nameof(dataAccess));
            _openSegment = openSegment;
        }

        public Task<IReadOnlyList<HiveStateChangeRecord>> GetStateChangesAsync(DateTime start, DateTime end)
        {
            return Task.Run<IReadOnlyList<HiveStateChangeRecord>>(() => Read(start, end));
        }

        private IReadOnlyList<HiveStateChangeRecord> Read(DateTime start, DateTime end)
        {
            List<HealthData> segments = _dataAccess.GetHealthDataBetween(start, end);

            HealthData open = _openSegment == null ? null : _openSegment();
            if (open != null)
                segments.Add(open);

            var changes = new List<HiveStateChangeRecord>();
            DateTime? coveredTo = null;

            foreach (HealthData segment in segments
                .Where(s => StateOf(s.HealthCategory) != NoRecord)
                .OrderBy(s => s.StartTime))
            {
                DateTime from = segment.StartTime < start ? start : segment.StartTime;
                DateTime to = segment.EndTime > end ? end : segment.EndTime;

                // Segments follow one another; one that overlaps the last goes on after it.
                if (coveredTo.HasValue && from < coveredTo.Value)
                    from = coveredTo.Value;
                if (to <= from)
                    continue;

                if (coveredTo.HasValue && from > coveredTo.Value)
                    changes.Add(new HiveStateChangeRecord { Time = coveredTo.Value, State = NoRecord });

                changes.Add(new HiveStateChangeRecord { Time = from, State = StateOf(segment.HealthCategory) });
                coveredTo = to;
            }

            if (coveredTo.HasValue && coveredTo.Value < end)
                changes.Add(new HiveStateChangeRecord { Time = coveredTo.Value, State = NoRecord });

            return changes;
        }

        /// <summary>ShiftStatusHelper's categories as the chart's HIVE states.</summary>
        private static int StateOf(string category)
        {
            switch (category)
            {
                case "RunTime": return 1;
                case "IdleTime": return 2;
                case "DownTime": return 5;
                default: return NoRecord;
            }
        }
    }
}
