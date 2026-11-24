using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;
using TealVacuum = Teal.Library.Vacuum.Vacuum;
using Nova.Alarm;
using Nova.Wpf.App.Services.Twincat.HmiDisplay;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Nova <see cref="IVacuum"/> over one Beckhoff library vacuum. Vacuum/Blow/Off ask the
    /// PLC through the library, which owns the valve outputs. While a screen follows the vacuum
    /// (<see cref="FollowStatus"/>), the library reads its current status first
    /// (UpdateVacuumProperties), then the PLC pushes every change of its HMI display struct -
    /// where the library says it is, read as the struct App.config's VacuumHmiDisplayType
    /// names - through <see cref="PlcSubscriptions"/>. Reading it never waits on the PLC.
    /// </summary>
    public sealed class TwincatVacuumAdapter : IVacuum, ILiveStatus, ITwincatStatusSource, ILibraryStatusSource
    {
        private readonly TealVacuum _vacuum;
        private readonly TwincatPlcConnection _plc;
        private readonly HmiDisplayStatus _display;

        /// <param name="displayType">The struct App.config's VacuumHmiDisplayType names; null when it names none that fits.</param>
        public TwincatVacuumAdapter(
            string componentId, TealVacuum vacuum, TwincatPlcConnection plc, PlcSubscriptions subscriptions, Type displayType, string feedbackAddress)
        {
            _vacuum = vacuum ?? throw new ArgumentNullException(nameof(vacuum));
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _display = new HmiDisplayStatus(subscriptions, plc, vacuum, displayType, vacuum.UpdateVacuumProperties);

            ComponentId = componentId;
            VacuumName = _vacuum.Name ?? componentId;
            FeedbackAddress = feedbackAddress ?? _vacuum.VacuumSense ?? "";
        }

        public string ComponentId { get; }
        public string VacuumName { get; }
        public string FeedbackAddress { get; }
        public IReadOnlyList<string> WatchedSymbols => _display.WatchedSymbols;

        public event EventHandler LibraryStatusRead
        {
            add { _display.LibraryRead += value; }
            remove { _display.LibraryRead -= value; }
        }

        public IDisposable FollowStatus() => _display.Follow();

        /// <summary>
        /// The library's commands are void and log their own failures, so a request that
        /// did not take shows in the log rather than in the returned task.
        /// </summary>
        public Task<bool> VacuumOnAsync() => RunLocked(_vacuum.VacuumOn);

        public Task<bool> BlowOnAsync() => RunLocked(_vacuum.BlowOn);

        public Task<bool> OffAsync() => RunLocked(_vacuum.Off);

        /// <summary>
        /// The HMI display the PLC last pushed - or, until it pushes one, the vacuum feedback
        /// the library read when the vacuum was followed. Never touches the PLC; with neither
        /// everything reads off.
        /// </summary>
        public VacuumStatusSnapshot ReadStatus()
        {
            var snapshot = new VacuumStatusSnapshot { ComponentId = ComponentId };

            IVacuumHmiDisplay display;
            string reason;
            if (_display.TryRead(out display, out reason))
                display.Fill(snapshot);
            else if (_display.HasLibraryStatus)
                snapshot.FeedbackOn = _vacuum.IsVacuumOn;

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
