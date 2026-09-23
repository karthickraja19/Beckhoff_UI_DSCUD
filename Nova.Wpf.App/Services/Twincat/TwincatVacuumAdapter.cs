using System;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;
using TealVacuum = Teal.Library.Vacuum.Vacuum;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Nova <see cref="IVacuum"/> over one Beckhoff library vacuum. Vacuum/Blow/Off ask the
    /// PLC through the library, which owns the valve outputs; status is the sensor and the
    /// two outputs as the PLC holds them, so the Manual board shows what the PLC reports.
    /// </summary>
    public sealed class TwincatVacuumAdapter : IVacuum
    {
        private readonly TealVacuum _vacuum;
        private readonly TwincatPlcConnection _plc;

        public TwincatVacuumAdapter(string componentId, TealVacuum vacuum, TwincatPlcConnection plc, string feedbackAddress)
        {
            _vacuum = vacuum ?? throw new ArgumentNullException(nameof(vacuum));
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));

            ComponentId = componentId;
            VacuumName = _vacuum.Name ?? componentId;
            FeedbackAddress = feedbackAddress ?? _vacuum.VacuumSense ?? "";
        }

        public string ComponentId { get; }
        public string VacuumName { get; }
        public string FeedbackAddress { get; }

        /// <summary>
        /// The library's commands are void and log their own failures, so a request that
        /// did not take shows in the log rather than in the returned task.
        /// </summary>
        public Task<bool> VacuumOnAsync() => RunLocked(_vacuum.VacuumOn);

        public Task<bool> BlowOnAsync() => RunLocked(_vacuum.BlowOn);

        public Task<bool> OffAsync() => RunLocked(_vacuum.Off);

        /// <summary>Called off the IO poll thread. Never throws: a failed read reports everything off.</summary>
        public VacuumStatusSnapshot ReadStatus()
        {
            var snapshot = new VacuumStatusSnapshot { ComponentId = ComponentId };

            try
            {
                lock (_plc.SyncRoot)
                {
                    if (!_plc.IsConnected)
                        return snapshot;

                    snapshot.FeedbackOn = _vacuum.IsVacuumOn;
                    snapshot.VacuumCommanded = _vacuum.IsVacuumCommanded;
                    snapshot.BlowCommanded = _vacuum.IsBlowCommanded;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ComponentId + " read failed: " + ex.Message);
            }

            return snapshot;
        }

        private Task<bool> RunLocked(Action action)
        {
            return Task.Run(() =>
            {
                lock (_plc.SyncRoot)
                {
                    try
                    {
                        if (!_plc.IsConnected)
                            return false;

                        action();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(ComponentId + " command failed: " + ex.Message);
                        return false;
                    }
                }
            });
        }
    }
}
