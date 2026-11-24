using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nova.Hive;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// The health check before the first unit after a nozzle change (S3.3 PD-12, "MES check
    /// Nozzle / tunnel Health status data (Input of the first product)").
    /// </summary>
    /// <remarks>
    /// Every switch of HIVE to Running asks MES for the health of each nozzle changed since
    /// the last check (<see cref="PlannedDowntimeProcedures.CheckChangedNozzlesAsync"/>). A
    /// nozzle MES fails, or one with no health left, puts the machine back into Nozzle Change
    /// (PD-12) through <see cref="HiveService.ReportFault"/>, which also asks the PLC to stop
    /// feeding. With no MES configured the check is skipped and logged; an MES that does not
    /// answer leaves the check pending for the next switch to Running.
    /// </remarks>
    public sealed class NozzleHealthGuard : IDisposable
    {
        private readonly HiveService _hive;
        private readonly PlannedDowntimeProcedures _procedures;
        private int _checking;

        public NozzleHealthGuard(HiveService hive, PlannedDowntimeProcedures procedures)
        {
            _hive = hive ?? throw new ArgumentNullException(nameof(hive));
            _procedures = procedures ?? throw new ArgumentNullException(nameof(procedures));
        }

        public void Start()
        {
            _hive.StateChanged += OnStateChanged;
        }

        public void Dispose()
        {
            _hive.StateChanged -= OnStateChanged;
        }

        private void OnStateChanged(object sender, int state)
        {
            if (state != HiveStateMachine.Running)
                return;

            // Never on the thread that changed the state: MES can take seconds.
            Task.Run(() => CheckAsync());
        }

        /// <summary>One check; true when production may go on.</summary>
        public async Task<bool> CheckAsync()
        {
            if (Interlocked.Exchange(ref _checking, 1) == 1)
                return true;

            try
            {
                string refused = await _procedures.CheckChangedNozzlesAsync().ConfigureAwait(false);
                if (refused == null)
                    return true;

                PlannedDowntimeReason nozzleChange = PlannedDowntimeReason.All.First(r => r.Code == "PD-12");
                _hive.ReportFault(nozzleChange.ToError(refused));
                return false;
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "PD-12", "The nozzle health check before the first unit failed: " + ex.Message, ex.ToString());
                return true;
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
            }
        }
    }
}
