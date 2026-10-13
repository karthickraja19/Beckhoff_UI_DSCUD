using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;
using TealCylinder = Teal.Library.Cylinder.Cylinder;
using Nova.Alarm;
using Nova.Wpf.App.Services.Twincat.HmiDisplay;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Nova <see cref="ICylinder"/> over one Beckhoff library cylinder bound to its valve
    /// outputs and reed inputs in the PLC. Extend/Retract go through the library. While a
    /// screen follows the cylinder (<see cref="FollowStatus"/>), the library reads its current
    /// status first (UpdateCylinderProperties), then the PLC pushes every change of its HMI
    /// display struct - where the library says it is, read as the struct App.config's
    /// CylinderHmiDisplayType names - through <see cref="PlcSubscriptions"/>. Reading it never
    /// waits on the PLC.
    ///
    /// The library has no Stop, so dropping both valves is done here (marked below). Move it
    /// into the library when it grows the API.
    /// </summary>
    public sealed class TwincatCylinderAdapter : ICylinder, ILiveStatus, ITwincatStatusSource, ILibraryStatusSource
    {
        private readonly TealCylinder _cylinder;
        private readonly TwincatPlcConnection _plc;
        private readonly HmiDisplayStatus _display;
        private readonly string[] _extendSenseSymbols;
        private readonly string[] _retractSenseSymbols;

        /// <summary>
        /// A single-valve cylinder is held extended by one coil and springs back when it is
        /// dropped, so it has no retract output to clear.
        /// </summary>
        private readonly bool _singleValve;

        /// <param name="displayType">The struct App.config's CylinderHmiDisplayType names; null when it names none that fits.</param>
        public TwincatCylinderAdapter(
            string componentId,
            TealCylinder cylinder,
            TwincatPlcConnection plc,
            PlcSubscriptions subscriptions,
            Type displayType,
            IEnumerable<string> extendSenseAddresses = null,
            IEnumerable<string> retractSenseAddresses = null)
        {
            _cylinder = cylinder ?? throw new ArgumentNullException(nameof(cylinder));
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _display = new HmiDisplayStatus(subscriptions, plc, cylinder, displayType, cylinder.UpdateCylinderProperties);

            ComponentId = componentId;
            CylinderName = _cylinder.Name ?? componentId;
            ExtendOutputAddress = _cylinder.ExtendName ?? "";
            RetractOutputAddress = _cylinder.RetractName ?? "";
            _singleValve = string.IsNullOrEmpty(RetractOutputAddress);

            _extendSenseSymbols = (_cylinder.ExtendSenses ?? new List<string>()).ToArray();
            _retractSenseSymbols = (_cylinder.RetractSenses ?? new List<string>()).ToArray();

            // The wiring address (X0-01) is what an electrician looks for, so the board
            // shows that where Config.csv maps one, and the PLC name otherwise.
            ExtendSenseAddresses = Display(extendSenseAddresses, _extendSenseSymbols);
            RetractSenseAddresses = Display(retractSenseAddresses, _retractSenseSymbols);
        }

        public string ComponentId { get; }
        public string CylinderName { get; }
        public string ExtendOutputAddress { get; }
        public string RetractOutputAddress { get; }
        public IReadOnlyList<string> ExtendSenseAddresses { get; }
        public IReadOnlyList<string> RetractSenseAddresses { get; }
        public IReadOnlyList<string> WatchedSymbols => _display.WatchedSymbols;

        public event EventHandler LibraryStatusRead
        {
            add { _display.LibraryRead += value; }
            remove { _display.LibraryRead -= value; }
        }

        public IDisposable FollowStatus() => _display.Follow();

        /// <summary>
        /// Fires the extend valve through the library. The PLC's own interlocks still apply.
        /// The library's Extend is void and logs its own failures, so a write that did not
        /// take shows in the log rather than in the returned task.
        /// </summary>
        public Task<bool> ExtendAsync() => RunLocked(() =>
        {
            _cylinder.Extend();
            return true;
        });

        public Task<bool> RetractAsync() => RunLocked(() =>
        {
            _cylinder.Retract();
            return true;
        });

        /// <summary>
        /// Drops both valves. On a double-valve cylinder that leaves the piston where the
        /// air pressure holds it; on a single-valve one it springs home.
        /// The library has no Stop, so this writes the request bits directly.
        /// </summary>
        public Task<bool> StopAsync() => RunLocked(() =>
        {
            bool extendCleared = ClearValve(_cylinder.ExtendName);
            bool retractCleared = _singleValve || ClearValve(_cylinder.RetractName);
            return extendCleared && retractCleared;
        });

        /// <summary>
        /// The HMI display the PLC last pushed - or, until it pushes one, what the library read
        /// when the cylinder was followed. Never touches the PLC, so it is safe on any thread.
        /// With neither every reed reads off and neither button is offered, which the Manual
        /// board draws as an unknown position.
        /// </summary>
        public CylinderStatusSnapshot ReadStatus()
        {
            var snapshot = new CylinderStatusSnapshot
            {
                ComponentId = ComponentId,
                ExtendSenseStates = new bool[_extendSenseSymbols.Length],
                RetractSenseStates = new bool[_retractSenseSymbols.Length],
            };

            ICylinderHmiDisplay display;
            string reason;
            if (_display.TryRead(out display, out reason))
            {
                display.Fill(snapshot);

                // The PLC owns the interlocks and refuses a move that is not safe; the board
                // only holds back while the position cannot be trusted.
                snapshot.SafeToExtend = true;
                snapshot.SafeToRetract = true;
            }
            else if (_display.HasLibraryStatus)
            {
                // The library reports one flag per side, so each side's reed chips show it.
                SetAll(snapshot.ExtendSenseStates, _cylinder.IsExtended);
                SetAll(snapshot.RetractSenseStates, _cylinder.IsRetracted);
                snapshot.IsExtended = _cylinder.IsExtended;
                snapshot.IsRetracted = _cylinder.IsRetracted;
                snapshot.IsBothSensed = _cylinder.IsExtended && _cylinder.IsRetracted;
                snapshot.SafeToExtend = _cylinder.IsSafeToExtend;
                snapshot.SafeToRetract = _cylinder.IsSafeToRetract;
            }
            else
            {
                return snapshot;
            }

            // Both sides sensed at once is impossible on a healthy cylinder - a stuck or
            // mis-wired reed. Neither position is trusted while it lasts, and neither
            // button is offered.
            if (snapshot.IsBothSensed)
            {
                snapshot.IsExtended = false;
                snapshot.IsRetracted = false;
                snapshot.SafeToExtend = false;
                snapshot.SafeToRetract = false;
            }

            return snapshot;
        }

        private static void SetAll(bool[] states, bool value)
        {
            for (int i = 0; i < states.Length; i++)
                states[i] = value;
        }

        /// <summary>
        /// Clears one valve request bit. Write reads the bit back, which is what confirms
        /// the valve took: unlike a stepper command bit, the PLC does not clear a valve
        /// output by itself.
        /// </summary>
        private bool ClearValve(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                return true;

            return _plc.IsConnected && _plc.Card.Write<bool>(symbol, false);
        }

        private static IReadOnlyList<string> Display(IEnumerable<string> addresses, string[] symbols)
        {
            var list = (addresses ?? Enumerable.Empty<string>()).ToList();
            while (list.Count < symbols.Length)
                list.Add(symbols[list.Count]);

            for (int i = 0; i < list.Count && i < symbols.Length; i++)
                if (string.IsNullOrWhiteSpace(list[i]))
                    list[i] = symbols[i];

            return list;
        }

        private Task<bool> RunLocked(Func<bool> action)
        {
            return Task.Run(() =>
            {
                lock (_plc.SyncRoot)
                {
                    try
                    {
                        return action();
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
