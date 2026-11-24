using System;
using System.Collections.Generic;
using System.Linq;
using Nova.Abstractions.Data.Repositories;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// The name of the shift running now, from the shift master in TEAL_Master, for records
    /// the HMI stamps itself (glue weighings). The shift list is read again every hour; with
    /// no database or no shift covering now the name is empty.
    /// </summary>
    public sealed class CurrentShift
    {
        private static readonly TimeSpan Reload = TimeSpan.FromHours(1);

        private readonly IShiftSettingsRepository _shifts;
        private readonly object _sync = new object();
        private IReadOnlyList<ShiftDefinition> _cached = new ShiftDefinition[0];
        private DateTime _readAt = DateTime.MinValue;

        public CurrentShift(IShiftSettingsRepository shifts)
        {
            _shifts = shifts;
        }

        public string Name()
        {
            if (_shifts == null)
                return string.Empty;

            IReadOnlyList<ShiftDefinition> shifts;
            lock (_sync)
            {
                if (DateTime.Now - _readAt > Reload)
                {
                    try
                    {
                        _cached = _shifts.GetShiftsAsync().GetAwaiter().GetResult() ?? new ShiftDefinition[0];
                    }
                    catch (Exception)
                    {
                        // Keep the last list: a database hiccup must not lose the shift name.
                    }

                    _readAt = DateTime.Now;
                }

                shifts = _cached;
            }

            TimeSpan now = DateTime.Now.TimeOfDay;
            ShiftDefinition current = shifts.FirstOrDefault(s => s != null && s.StartTime != s.EndTime
                && (s.StartTime < s.EndTime
                    ? now >= s.StartTime && now < s.EndTime
                    : now >= s.StartTime || now < s.EndTime));
            return current != null ? current.ShiftName ?? string.Empty : string.Empty;
        }
    }
}
