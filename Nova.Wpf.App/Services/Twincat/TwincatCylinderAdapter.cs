using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;
using TealCylinder = Teal.Library.Cylinder.Cylinder;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Nova <see cref="ICylinder"/> over one Beckhoff library cylinder bound to its valve
    /// outputs and reed inputs in the PLC. Extend/Retract go through the library, which
    /// writes the request bits; status comes from
    /// <see cref="TealCylinder.UpdateCylinderProperties"/>, so the Manual board shows what
    /// the sensors report rather than what was last clicked.
    ///
    /// Two things the library does not expose yet are done here against the card directly,
    /// each marked below: the per-reed states the sensor chips need, and dropping both
    /// valves for Stop. Move them into the library when it grows the API.
    /// </summary>
    public sealed class TwincatCylinderAdapter : ICylinder
    {
        private readonly TealCylinder _cylinder;
        private readonly TwincatPlcConnection _plc;
        private readonly string[] _extendSenseSymbols;
        private readonly string[] _retractSenseSymbols;

        /// <summary>
        /// A single-valve cylinder is held extended by one coil and springs back when it is
        /// dropped, so it has no retract output to clear.
        /// </summary>
        private readonly bool _singleValve;

        public TwincatCylinderAdapter(
            string componentId,
            TealCylinder cylinder,
            TwincatPlcConnection plc,
            IEnumerable<string> extendSenseAddresses = null,
            IEnumerable<string> retractSenseAddresses = null)
        {
            _cylinder = cylinder ?? throw new ArgumentNullException(nameof(cylinder));
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));

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
        /// Called off the IO poll thread. Never throws: a failed read leaves the snapshot
        /// reporting nothing sensed, which the Manual board draws as an unknown position.
        /// </summary>
        public CylinderStatusSnapshot ReadStatus()
        {
            var snapshot = new CylinderStatusSnapshot
            {
                ComponentId = ComponentId,
                ExtendSenseStates = new bool[_extendSenseSymbols.Length],
                RetractSenseStates = new bool[_retractSenseSymbols.Length],
            };

            try
            {
                lock (_plc.SyncRoot)
                {
                    if (!_plc.IsConnected)
                        return snapshot;

                    // One round of PLC reads into the library's cached properties, which the
                    // four lines below then copy. Everything in the snapshot therefore comes
                    // from the same moment.
                    _cylinder.UpdateCylinderProperties();

                    snapshot.IsExtended = _cylinder.IsExtended;
                    snapshot.IsRetracted = _cylinder.IsRetracted;
                    snapshot.SafeToExtend = _cylinder.IsSafeToExtend;
                    snapshot.SafeToRetract = _cylinder.IsSafeToRetract;

                    // The library reports the position as one flag per side and keeps no
                    // per-reed state, so the sensor chips are read here.
                    ReadSenses(_extendSenseSymbols, snapshot.ExtendSenseStates);
                    ReadSenses(_retractSenseSymbols, snapshot.RetractSenseStates);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ComponentId + " read failed: " + ex.Message);
                return snapshot;
            }

            // Both sides sensed at once is impossible on a healthy cylinder - a stuck or
            // mis-wired reed. Neither position is trusted while it lasts, and neither
            // button is offered.
            if (snapshot.IsExtended && snapshot.IsRetracted)
            {
                snapshot.IsBothSensed = true;
                snapshot.IsExtended = false;
                snapshot.IsRetracted = false;
                snapshot.SafeToExtend = false;
                snapshot.SafeToRetract = false;
            }

            return snapshot;
        }

        private void ReadSenses(string[] symbols, bool[] states)
        {
            for (int i = 0; i < symbols.Length; i++)
                states[i] = !string.IsNullOrEmpty(symbols[i]) && _plc.Card.Read<bool>(symbols[i]);
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
