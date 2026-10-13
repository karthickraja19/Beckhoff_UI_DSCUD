using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Nova.Abstractions.Hardware.Interfaces;
using Master.Configuration;
using Master.Configuration.Model;
using Teal.Library.Servo;
using Teal.Library.Stepper;
using Teal.Library.TcpIp;
using Teal.Library.Twincat;
using NovaAxis = Nova.Abstractions.Hardware.IAxis;
using NovaCylinder = Nova.Abstractions.Hardware.ICylinder;
using NovaStepper = Nova.Abstractions.Hardware.IStepper;
using NovaVacuum = Nova.Abstractions.Hardware.IVacuum;
using TealCylinder = Teal.Library.Contracts.ICylinder;
using TealStepper = Teal.Library.Contracts.IStepper;
using TealVacuum = Teal.Library.Contracts.IVacuum;
using Nova.Alarm;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Puts the Beckhoff library devices Master.Configuration created
    /// (<see cref="DeviceObjects"/>, keyed by its enums) behind the Nova contracts. When a
    /// screen opens a device, its adapter has the library read the device's current status
    /// (its Update...Properties); after that the status is the device's HMI display struct -
    /// where the library says it is (<see cref="HmiDisplays.SymbolOf"/>), read as the struct
    /// App.config names for that kind of device, passed in here - pushed by the PLC through
    /// the shared <see cref="PlcSubscriptions"/>. Status is never polled.
    /// </summary>
    /// <remarks>
    /// The devices are taken in the order of their enum, and each adapter's ComponentId is
    /// the name of its enum member - the Nova contracts cannot reference Master.Configuration.
    /// A member with no device - not configured, or not created, both reported at startup -
    /// is left out. When the PLC is up, each device's HMI display and the symbols the library
    /// commands are checked against the PLC's declarations, so a wrong Id, name or struct is
    /// reported at startup instead of as a screen that silently shows nothing.
    /// </remarks>
    public static class TwincatDevices
    {
        /// <summary>
        /// One adapter per stepper, for the Stepper tab. Every stepper is an I/O stepper: one
        /// driven as an NC axis is a servo, created by <see cref="CreateAxes"/>.
        /// </summary>
        /// <param name="displayType">The struct App.config's IOStepperHmiDisplayType names; null when it names none that fits.</param>
        public static List<NovaStepper> CreateSteppers(
            TwincatPlcConnection plc,
            PlcSubscriptions subscriptions,
            List<StepperConfiguration> configs,
            IReadOnlyDictionary<Steppers, TealStepper> created,
            Type displayType,
            ICollection<string> warnings)
        {
            var steppers = new List<NovaStepper>();
            var displays = new DisplayCheck("I/O steppers", plc, displayType);

            foreach (var (name, _, device) in Created(configs, c => c.EnumName, created))
            {
                var stepper = device as IOStepper;
                if (stepper == null)
                    continue;

                displays.Check(name.ToString(), stepper);
                steppers.Add(new TwincatStepperAdapter(name.ToString(), stepper, plc, subscriptions, displayType));
            }

            displays.Report(warnings);
            return steppers;
        }

        /// <summary>
        /// One adapter per cylinder. The valve and reed symbols the library commands are the
        /// PLC names from Cylinders.json under App.config's PlcIoGvl - for example
        /// GVL_IO.Light_Mask_ZAxis_Cyl_FW.
        /// </summary>
        /// <param name="displayType">The struct App.config's CylinderHmiDisplayType names; null when it names none that fits.</param>
        /// <param name="resolveAddress">
        /// Maps a PLC name to its wiring address from Data\Config.csv, for display on the
        /// Manual board. Pass null to show the PLC names instead.
        /// </param>
        public static List<NovaCylinder> CreateCylinders(
            TwincatPlcConnection plc,
            PlcSubscriptions subscriptions,
            List<CylinderConfiguration> configs,
            IReadOnlyDictionary<Cylinders, TealCylinder> created,
            Type displayType,
            Func<string, bool, string> resolveAddress,
            ICollection<string> warnings)
        {
            var cylinders = new List<NovaCylinder>();
            var displays = new DisplayCheck("cylinders", plc, displayType);

            foreach (var (name, cfg, device) in Created(configs, c => c.EnumName, created))
            {
                var cylinder = device as Teal.Library.Cylinder.Cylinder;
                if (cylinder == null)
                    continue;

                if (displays.Connected)
                {
                    var missing = new[] { cylinder.ExtendName, cylinder.RetractName }
                        .Concat(cylinder.ExtendSenses ?? new List<string>())
                        .Concat(cylinder.RetractSenses ?? new List<string>())
                        .Where(s => !string.IsNullOrEmpty(s) && !plc.SymbolExists(s))
                        .ToList();

                    if (missing.Count > 0)
                    {
                        warnings.Add(name + ": " + string.Join(", ", missing)
                            + " " + (missing.Count == 1 ? "does" : "do")
                            + " not exist in the PLC. Check the names in Data\\Cylinders.json "
                            + "and the PlcIoGvl setting in App.config.");
                    }
                }

                displays.Check(name.ToString(), cylinder);
                cylinders.Add(new TwincatCylinderAdapter(
                    name.ToString(),
                    cylinder,
                    plc,
                    subscriptions,
                    displayType,
                    Addresses(Names(cfg.ExtendSenses), resolveAddress),
                    Addresses(Names(cfg.RetractSenses), resolveAddress)));
            }

            displays.Report(warnings);
            return cylinders;
        }

        /// <summary>
        /// One adapter per vacuum. As with the cylinders, the sensor and output names in
        /// Vacuums.json are PLC variable names under PlcIoGvl.
        /// </summary>
        /// <param name="displayType">The struct App.config's VacuumHmiDisplayType names; null when it names none that fits.</param>
        public static List<NovaVacuum> CreateVacuums(
            TwincatPlcConnection plc,
            PlcSubscriptions subscriptions,
            List<VacuumConfiguration> configs,
            IReadOnlyDictionary<Vacuums, TealVacuum> created,
            Type displayType,
            Func<string, bool, string> resolveAddress,
            ICollection<string> warnings)
        {
            var vacuums = new List<NovaVacuum>();
            var displays = new DisplayCheck("vacuums", plc, displayType);

            foreach (var (name, cfg, device) in Created(configs, c => c.EnumName, created))
            {
                var vacuum = device as Teal.Library.Vacuum.Vacuum;
                if (vacuum == null)
                    continue;

                if (displays.Connected)
                {
                    var missing = new[] { vacuum.VacuumSense, vacuum.VacuumOutput, vacuum.BlowOutput }
                        .Where(s => !string.IsNullOrEmpty(s) && !plc.SymbolExists(s))
                        .ToList();

                    if (missing.Count > 0)
                    {
                        warnings.Add(name + ": " + string.Join(", ", missing)
                            + " " + (missing.Count == 1 ? "does" : "do")
                            + " not exist in the PLC. Check the names in Data\\Vacuums.json "
                            + "and the PlcIoGvl setting in App.config.");
                    }
                }

                string feedback = string.IsNullOrWhiteSpace(cfg.VacuumSense) || resolveAddress == null
                    ? vacuum.VacuumSense
                    : resolveAddress(cfg.VacuumSense, false);

                displays.Check(name.ToString(), vacuum);
                vacuums.Add(new TwincatVacuumAdapter(name.ToString(), vacuum, plc, subscriptions, displayType, feedback));
            }

            displays.Report(warnings);
            return vacuums;
        }

        /// <summary>One adapter per servo.</summary>
        /// <param name="displayType">The struct App.config's ServoHmiDisplayType names; null when it names none that fits.</param>
        public static List<NovaAxis> CreateAxes(
            TwincatPlcConnection plc,
            PlcSubscriptions subscriptions,
            List<ServoConfiguration> configs,
            IReadOnlyDictionary<Servos, AxisServo> created,
            Type displayType,
            ICollection<string> warnings)
        {
            var axes = new List<NovaAxis>();
            var displays = new DisplayCheck("servos", plc, displayType);

            foreach (var (name, _, servo) in Created(configs, c => c.EnumName, created))
            {
                displays.Check(name.ToString(), servo);
                axes.Add(new TwincatAxisAdapter(name.ToString(), servo, plc, subscriptions, displayType));
            }

            displays.Report(warnings);
            return axes;
        }

        /// <summary>
        /// The robots, each with the name it is shown by, for the Robot tab's I/O. When the PLC is
        /// up, each one's I/O image is checked against the PLC's declaration - the library reads
        /// typeRobotFBHwInputs and typeRobotFBHwOutputs by their members' offsets, so a different
        /// size means the two have drifted - and its connection flag is checked to exist.
        /// </summary>
        public static List<KeyValuePair<string, TwincatRobot>> CreateRobots(
            TwincatPlcConnection plc,
            List<RobotConfiguration> configs,
            IReadOnlyDictionary<Robots, TwincatRobot> created,
            ICollection<string> warnings)
        {
            var robots = new List<KeyValuePair<string, TwincatRobot>>();
            bool connected = plc.IsConnected;

            foreach (var (name, _, robot) in Created(configs, c => c.EnumName, created))
            {
                if (connected)
                {
                    CheckImage(plc, name.ToString(), robot.InputSymbol, robot.InputSize, "typeRobotFBHwInputs", warnings);
                    CheckImage(plc, name.ToString(), robot.OutputSymbol, robot.OutputSize, "typeRobotFBHwOutputs", warnings);
                    if (!plc.SymbolExists(robot.ConnectedSymbol))
                    {
                        warnings.Add(name + ": " + robot.ConnectedSymbol + " does not exist in the PLC, so the Robot tab "
                            + "shows it disconnected. Check its Id in Data\\Robots.json - the PLC array starts at 1.");
                    }
                }

                robots.Add(new KeyValuePair<string, TwincatRobot>(
                    string.IsNullOrWhiteSpace(robot.Name) ? name.ToString() : robot.Name, robot));
            }

            return robots;
        }

        private static void CheckImage(TwincatPlcConnection plc, string name, string symbol, int size, string type, ICollection<string> warnings)
        {
            int? declared = plc.SymbolSize(symbol);
            if (declared == null)
            {
                warnings.Add(name + ": " + symbol + " does not exist in the PLC, so the Robot tab shows its points at 0. "
                    + "Check its Id in Data\\Robots.json.");
            }
            else if (declared.Value != size)
            {
                warnings.Add(name + ": " + symbol + " is " + declared.Value + " bytes in the PLC but the library's " + type
                    + " is " + size + ", so the Robot tab shows wrong values - the PLC struct and "
                    + "Teal.Library.Twincat\\RobotHwIO.cs must declare the same members in the same order.");
            }
        }

        /// <summary>
        /// One adapter per TCP/IP device of one kind (CCDs or scanners), each with its
        /// entry in the file, for the console tabs to show its address. When the PLC is up,
        /// each binding is checked, so a wrong Id or DUT name is reported at startup.
        /// </summary>
        /// <param name="kind">What the devices are called in the startup warnings - "CCDs", "scanners".</param>
        /// <param name="displayType">The struct App.config names for this kind of device; null when it names none that fits.</param>
        public static List<KeyValuePair<TConfig, INetworkProtocol>> CreateTcpDevices<TEnum, TConfig>(
            TwincatPlcConnection plc,
            PlcSubscriptions subscriptions,
            string kind,
            List<TConfig> configs,
            IReadOnlyDictionary<TEnum, TcpDevice> created,
            Type displayType,
            ICollection<string> warnings)
            where TEnum : struct, Enum
            where TConfig : NetworkDeviceConfiguration
        {
            var devices = new List<KeyValuePair<TConfig, INetworkProtocol>>();
            var displays = new DisplayCheck(kind, plc, displayType);

            foreach (var (name, cfg, device) in Created(configs, c => c.EnumName, created))
            {
                if (displays.Connected && !plc.SymbolExists(device.Instance + ".status.connected"))
                {
                    warnings.Add(name + ": " + device.Instance + " does not exist in the PLC. Check its Id in the "
                        + "device file and its DUT name in Data\\CardsConfiguration.json - the PLC array starts at 1.");
                }

                displays.Check(name.ToString(), device);
                devices.Add(new KeyValuePair<TConfig, INetworkProtocol>(
                    cfg, new TwincatTcpDeviceAdapter(device, plc, subscriptions, displayType)));
            }

            displays.Report(warnings);
            return devices;
        }

        /// <summary>Each device Master.Configuration created, with its enum member and its entry in the file, in the enum's order.</summary>
        private static IEnumerable<(TEnum Name, TConfig Config, TDevice Device)> Created<TEnum, TConfig, TDevice>(
            List<TConfig> configs, Func<TConfig, string> enumName, IReadOnlyDictionary<TEnum, TDevice> created)
            where TEnum : struct, Enum
            where TConfig : class
        {
            foreach (var configured in MachineConfiguration.Configured<TEnum, TConfig>(configs, enumName))
            {
                TDevice device;
                if (created.TryGetValue(configured.Key, out device))
                    yield return (configured.Key, configured.Value, device);
            }
        }

        private static List<string> Names(List<string> configured)
        {
            return configured == null
                ? new List<string>()
                : configured.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        }

        private static List<string> Addresses(List<string> names, Func<string, bool, string> resolveAddress)
        {
            return resolveAddress == null
                ? names
                : names.Select(n => resolveAddress(n, false)).ToList();
        }

        /// <summary>
        /// Checks the HMI display of each device of one kind, and reports the problems together
        /// rather than once per device: the devices the library gives no symbol for, and - when
        /// the PLC is up - the symbols it does not declare, or declares at another size than
        /// the struct App.config names.
        /// </summary>
        private sealed class DisplayCheck
        {
            private readonly string _kind;
            private readonly TwincatPlcConnection _plc;
            private readonly Type _type;
            private readonly List<string> _noSymbol = new List<string>();
            private readonly List<string> _notInPlc = new List<string>();
            private readonly SortedDictionary<int, List<string>> _wrongSize = new SortedDictionary<int, List<string>>();

            public DisplayCheck(string kind, TwincatPlcConnection plc, Type type)
            {
                _kind = kind;
                _plc = plc;
                _type = type;
                Connected = plc.IsConnected;
            }

            /// <summary>The PLC was up when the check began, so symbols can be checked against it.</summary>
            public bool Connected { get; }

            public void Check(string name, object libraryDevice)
            {
                string symbol = HmiDisplays.SymbolOf(libraryDevice);
                if (symbol == null)
                {
                    _noSymbol.Add(name);
                    return;
                }

                if (!Connected)
                    return;

                int? size = _plc.SymbolSize(symbol);
                if (size == null)
                {
                    _notInPlc.Add(name + " (" + symbol + ")");
                }
                else if (_type != null && size.Value != Marshal.SizeOf(_type))
                {
                    List<string> names;
                    if (!_wrongSize.TryGetValue(size.Value, out names))
                        _wrongSize[size.Value] = names = new List<string>();

                    names.Add(name);
                }
            }

            public void Report(ICollection<string> warnings)
            {
                if (_noSymbol.Count > 0)
                {
                    warnings.Add("The library gives no " + HmiDisplays.SymbolProperty + " for these " + _kind
                        + ", so they show what the library reads when a screen opens them, but no changes after: "
                        + string.Join(", ", _noSymbol) + ".");
                }

                if (_notInPlc.Count > 0)
                {
                    warnings.Add("The HMI displays of these " + _kind + " do not exist in the PLC, so they show no "
                        + "live status: " + string.Join(", ", _notInPlc) + ".");
                }

                foreach (KeyValuePair<int, List<string>> size in _wrongSize)
                {
                    warnings.Add("The HMI displays of these " + _kind + " are " + size.Key + " bytes in the PLC but "
                        + _type.Name + " is " + Marshal.SizeOf(_type) + ", so they show no live status - the PLC "
                        + "struct and Services\\Twincat\\HmiDisplay\\" + _type.Name + ".cs must declare the same "
                        + "fields in the same order: " + string.Join(", ", size.Value) + ".");
                }
            }
        }
    }
}
