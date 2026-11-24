using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Nova.Abstractions.Services;
using Nova.Hive;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// S3.1 PD-04 Valve Change V4.1, point 4: for a valve with an internal serial number the
    /// software reads the SN from the valve's controller every 5 minutes, on the clock, and
    /// compares it with the SN recorded in the configuration file.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Only gantries whose valve model has "Internal Serial Number" set (Setting &gt;
    /// Valve Model) and that have an internal SN recorded by a Valve Change are checked.</item>
    /// <item>A different SN, or a read that fails: HIVE goes to downtime with
    /// <see cref="ErrorCode"/> through <see cref="HiveService.ReportFault"/>, which also asks
    /// the PLC to finish the units in process and stop feeding.</item>
    /// <item>Paused while HIVE is in downtime, planned or not - except LAD (PD-11), which is
    /// "a special production mode" and stays checked. Paused while the PLC is in manual.</item>
    /// <item>The controller's SN is the dispenser block's <c>SerialNumber</c> member in the
    /// symbol map. With that blank the check is not available and the startup warnings say so.</item>
    /// </list>
    /// </remarks>
    public sealed class ValveSerialMonitor : IDisposable
    {
        /// <summary>DFM slide 32: glue error G05, "Controller Unresponsive", on the dispensing valve. Proposed; AME confirms.</summary>
        public const string ErrorCode = "G05DSVP-01";

        private const string LadCode = "PD-11";

        private readonly HiveService _hive;
        private readonly IConsumableStore _store;
        private readonly Func<int, string> _readSerial;
        private readonly int _minutes;
        private Timer _timer;
        private int _checking;

        /// <param name="readSerial">
        /// Reads the controller's valve SN of one gantry, by its place in the consumables
        /// (0 = the first gantry); throws when the read fails. Null: not available.
        /// </param>
        /// <param name="minutes">How often, on the clock; 0 or less switches the check off.</param>
        public ValveSerialMonitor(HiveService hive, IConsumableStore store, Func<int, string> readSerial, int minutes)
        {
            _hive = hive ?? throw new ArgumentNullException(nameof(hive));
            _store = store;
            _readSerial = readSerial;
            _minutes = minutes;
        }

        /// <summary>Gantries whose valve the check applies to: a model with an internal SN.</summary>
        public IReadOnlyList<string> CheckedGantries
        {
            get
            {
                if (_store == null)
                    return new string[0];

                return _store.Gantries.Where(HasInternalSn).Select(g => g.Gantry).ToList();
            }
        }

        public bool IsAvailable
        {
            get { return _readSerial != null && _store != null && _minutes > 0; }
        }

        public void Start()
        {
            if (!IsAvailable || _timer != null)
                return;

            _timer = new Timer(_ => Tick(), null, UntilNextSlot(DateTime.Now), Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            Timer timer = _timer;
            _timer = null;
            if (timer != null)
                timer.Dispose();
        }

        /// <summary>"Every 5 minutes on the clock": 10:00, 10:05, 10:10, ...</summary>
        private TimeSpan UntilNextSlot(DateTime now)
        {
            int minutesIntoHour = now.Minute % _minutes;
            DateTime slot = now.AddMinutes(_minutes - minutesIntoHour).AddSeconds(-now.Second).AddMilliseconds(-now.Millisecond);
            TimeSpan wait = slot - now;
            return wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromMinutes(_minutes) : wait;
        }

        private void Tick()
        {
            try
            {
                if (Interlocked.Exchange(ref _checking, 1) == 0)
                {
                    try
                    {
                        Check();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _checking, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                Log(LogType.Error, "The valve SN check failed: " + ex.Message);
            }
            finally
            {
                Timer timer = _timer;
                if (timer != null)
                {
                    try
                    {
                        timer.Change(UntilNextSlot(DateTime.Now), Timeout.InfiniteTimeSpan);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Closing down.
                    }
                }
            }
        }

        /// <summary>One round over the gantries. Returns what it found wrong, or null.</summary>
        public string Check()
        {
            if (!IsAvailable || Paused)
                return null;

            IReadOnlyList<GantryConsumables> gantries = _store.Gantries;
            for (int index = 0; index < gantries.Count; index++)
            {
                GantryConsumables gantry = gantries[index];
                if (!HasInternalSn(gantry))
                    continue;

                string recorded = (gantry.ValveInternalSn ?? string.Empty).Trim();
                if (recorded.Length == 0)
                {
                    Log(LogType.Info, "The " + gantry.Gantry + " gantry's valve has no internal SN recorded yet (do a Valve Change, PD-04): not checked.");
                    continue;
                }

                string problem;
                try
                {
                    string read = (_readSerial(index) ?? string.Empty).Trim();
                    problem = read.Length == 0
                        ? "the controller gave no valve SN"
                        : string.Equals(read, recorded, StringComparison.OrdinalIgnoreCase)
                            ? null
                            : "the controller reports valve SN " + read + ", the configuration file has " + recorded;
                }
                catch (Exception ex)
                {
                    problem = "the valve SN could not be read from the controller (" + ex.Message + ")";
                }

                if (problem == null)
                    continue;

                string detail = "Valve SN check on the " + gantry.Gantry + " gantry: " + problem
                    + ". Check the valve, then do a Valve Change (PD-04) if it was replaced.";
                _hive.ReportFault(new HiveError
                {
                    Code = ErrorCode,
                    Message = "Glue Error",
                    Detail = detail,
                    Severity = "error",
                    StopsMachine = true,
                });
                return detail;
            }

            return null;
        }

        /// <summary>4.3: paused in downtime and planned downtime, except in LAD; and while HIVE is off in manual.</summary>
        private bool Paused
        {
            get
            {
                if (_hive.IsSuspended)
                    return true;

                if (_hive.CurrentState != HiveStateMachine.Downtime)
                    return false;

                HiveError root = _hive.RootError;
                return root == null || !string.Equals(root.Code, LadCode, StringComparison.OrdinalIgnoreCase);
            }
        }

        private bool HasInternalSn(GantryConsumables gantry)
        {
            if (gantry == null || string.IsNullOrWhiteSpace(gantry.ValveModel))
                return false;

            ValveModel model = _store.ValveModels.FirstOrDefault(m => string.Equals(m.Name, gantry.ValveModel, StringComparison.OrdinalIgnoreCase));
            return model != null && model.InternalSn;
        }

        private static void Log(LogType type, string message)
        {
            Logger.WriteLog(type, "Hive", "ValveSn", message, string.Empty);
        }
    }
}
