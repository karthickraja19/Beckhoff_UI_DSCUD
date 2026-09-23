using System;
using System.Collections.Generic;
using System.Linq;
using Nova.Abstractions.Hardware.Interfaces;
using Nova.Core.Config;
using Teal.Library.Contracts;
using Teal.Library.Servo;
using Teal.Library.Stepper;
using Teal.Library.TcpIp;
using NovaAxis = Nova.Abstractions.Hardware.IAxis;
using NovaCylinder = Nova.Abstractions.Hardware.ICylinder;
using NovaStepper = Nova.Abstractions.Hardware.IStepper;
using NovaVacuum = Nova.Abstractions.Hardware.IVacuum;
using TealCylinder = Teal.Library.Cylinder.Cylinder;
using TealStepper = Teal.Library.Contracts.IStepper;
using TealVacuum = Teal.Library.Vacuum.Vacuum;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>Builds the TwinCAT device adapters from the machine profile under Data\.</summary>
    public static class TwincatDevices
    {
        /// <summary>
        /// One adapter per Steppers.json entry, bound to {PlcStepperGvl}.{PlcStepperArray}[Id].
        /// IsAxisEnabled picks the FB_STEPPER branch: false = I/O conveyor (IOStepper),
        /// true = NC axis (AxisStepper). When the PLC is up, each binding is checked against
        /// the PLC's symbols and the mode flag is written, so a wrong Id is reported at
        /// startup instead of as a button that silently does nothing.
        /// </summary>
        public static List<NovaStepper> CreateSteppers(
            TwincatPlcConnection plc, IEnumerable<StepperConfiguration> configs, ICollection<string> warnings)
        {
            var steppers = new List<NovaStepper>();
            bool connected = plc.IsConnected;

            foreach (var cfg in configs)
            {
                var properties = new StepperProperties
                {
                    Name = cfg.Name,
                    EnumName = cfg.EnumName,
                    Id = cfg.Id,
                    AxisEnabled = cfg.IsAxisEnabled,
                    Speed = cfg.Speed,
                    Acceleration = cfg.Acceleration,
                    Deacceleration = cfg.Deceleration,
                    Pitch = cfg.Pitch,
                    Pulse = cfg.Pulse,
                    PPR = cfg.PPR,
                    Card = plc.Card,
                };

                TealStepper stepper = cfg.IsAxisEnabled
                    ? (TealStepper)new AxisStepper(plc.Card, properties)
                    : new IOStepper(plc.Card, properties);

                string instance = plc.StepperInstance(cfg.Id);

                if (connected)
                {
                    if (!plc.SymbolExists(instance + ".config.axisstepper"))
                    {
                        warnings.Add(cfg.EnumName + ": " + instance + " does not exist in the PLC. "
                            + "Check its Id in Data\\Steppers.json - the PLC array starts at 1.");
                    }
                    else
                    {
                        bool modeWritten;
                        lock (plc.SyncRoot)
                        {
                            modeWritten = stepper.ApplyMode();
                        }

                        if (!modeWritten)
                            warnings.Add(cfg.EnumName + ": could not write " + instance + ".config.axisstepper.");
                    }
                }

                steppers.Add(new TwincatStepperAdapter(cfg.EnumName, instance, stepper, plc));
            }

            return steppers;
        }

        /// <summary>
        /// One adapter per Cylinders.json entry. The valve and reed names in that file are
        /// the PLC's variable names, so the symbol is {ioGvl}.{name} - for example
        /// GVL_IO.Light_Mask_ZAxis_Cyl_FW. Leave PlcIoGvl blank in App.config when the PLC
        /// declares them at the top level instead.
        ///
        /// When the PLC is up, every symbol is checked against its declarations, so a name
        /// that does not exist is reported at startup rather than as a button that silently
        /// does nothing or a reed chip that never lights.
        /// </summary>
        /// <param name="resolveAddress">
        /// Maps a PLC name to its wiring address from Data\Config.csv, for display on the
        /// Manual board. Pass null to show the PLC names instead.
        /// </param>
        public static List<NovaCylinder> CreateCylinders(
            TwincatPlcConnection plc,
            IEnumerable<CylinderConfiguration> configs,
            string ioGvl,
            Func<string, bool, string> resolveAddress,
            ICollection<string> warnings)
        {
            var cylinders = new List<NovaCylinder>();
            bool connected = plc.IsConnected;

            foreach (var cfg in configs)
            {
                var extendValves = Names(cfg.ExtendValves);
                var retractValves = Names(cfg.RetractValves);
                var extendSenses = Names(cfg.ExtendSenses);
                var retractSenses = Names(cfg.RetractSenses);

                // The library takes full PLC symbol paths, the way the stepper takes
                // GLOBAL_STEPPER.gStepper[Id].
                var properties = new CylinderProperties
                {
                    Name = cfg.Name,
                    EnumName = cfg.EnumName,
                    ExtendOutput = extendValves.Count > 0 ? Symbol(ioGvl, extendValves[0]) : "",
                    RetractOutput = retractValves.Count > 0 ? Symbol(ioGvl, retractValves[0]) : "",
                    ExtendSenses = extendSenses.Select(n => Symbol(ioGvl, n)).ToList(),
                    RetractSenses = retractSenses.Select(n => Symbol(ioGvl, n)).ToList(),
                };

                if (connected)
                {
                    var missing = new[] { properties.ExtendOutput, properties.RetractOutput }
                        .Concat(properties.ExtendSenses)
                        .Concat(properties.RetractSenses)
                        .Where(s => !string.IsNullOrEmpty(s) && !plc.SymbolExists(s))
                        .ToList();

                    if (missing.Count > 0)
                    {
                        warnings.Add(cfg.EnumName + ": " + string.Join(", ", missing)
                            + " " + (missing.Count == 1 ? "does" : "do")
                            + " not exist in the PLC. Check the names in Data\\Cylinders.json "
                            + "and the PlcIoGvl setting in App.config.");
                    }
                }

                cylinders.Add(new TwincatCylinderAdapter(
                    cfg.EnumName,
                    new TealCylinder(plc.Card, properties),
                    plc,
                    Addresses(extendSenses, resolveAddress),
                    Addresses(retractSenses, resolveAddress)));
            }

            return cylinders;
        }

        /// <summary>
        /// One adapter per Vacuums.json entry. As with the cylinders, the sensor and output
        /// names are PLC variable names under {ioGvl}, and a name the PLC does not declare
        /// is reported at startup.
        /// </summary>
        public static List<NovaVacuum> CreateVacuums(
            TwincatPlcConnection plc,
            IEnumerable<VacuumConfiguration> configs,
            string ioGvl,
            Func<string, bool, string> resolveAddress,
            ICollection<string> warnings)
        {
            var vacuums = new List<NovaVacuum>();
            bool connected = plc.IsConnected;

            foreach (var cfg in configs)
            {
                var properties = new VacuumProperties
                {
                    Name = cfg.Name,
                    EnumName = cfg.EnumName,
                    VacuumSense = Symbol(ioGvl, cfg.VacuumSense),
                    VacuumOutput = Symbol(ioGvl, cfg.VacuumOutput),
                    BlowOutput = Symbol(ioGvl, cfg.BlowOutput),
                };

                if (connected)
                {
                    var missing = new[] { properties.VacuumSense, properties.VacuumOutput, properties.BlowOutput }
                        .Where(s => !string.IsNullOrEmpty(s) && !plc.SymbolExists(s))
                        .ToList();

                    if (missing.Count > 0)
                    {
                        warnings.Add(cfg.EnumName + ": " + string.Join(", ", missing)
                            + " " + (missing.Count == 1 ? "does" : "do")
                            + " not exist in the PLC. Check the names in Data\\Vacuums.json "
                            + "and the PlcIoGvl setting in App.config.");
                    }
                }

                string feedback = string.IsNullOrWhiteSpace(cfg.VacuumSense) || resolveAddress == null
                    ? properties.VacuumSense
                    : resolveAddress(cfg.VacuumSense, false);

                vacuums.Add(new TwincatVacuumAdapter(cfg.EnumName, new TealVacuum(plc.Card, properties), plc, feedback));
            }

            return vacuums;
        }

        /// <summary>One adapter per Servos.json entry.</summary>
        public static List<NovaAxis> CreateAxes(TwincatPlcConnection plc, IEnumerable<ServoConfiguration> configs)
        {
            var axes = new List<NovaAxis>();

            foreach (var cfg in configs)
            {
                var properties = new AxisProperties
                {
                    Id = cfg.Id,
                    Name = cfg.Name,
                    EnumName = cfg.EnumName,
                    Pulse = cfg.Pulse,
                    PositiveLimit = cfg.SoftLimits != null ? cfg.SoftLimits.Positive : 0,
                    NegativeLimit = cfg.SoftLimits != null ? cfg.SoftLimits.Negative : 0,
                    Velocity = cfg.Speed,
                    Acceleration = cfg.Acceleration,
                    Deceleration = cfg.Deceleration,
                    Type = MotorType.Servo,
                };

                axes.Add(new TwincatAxisAdapter(cfg.EnumName, new AxisServo(plc.Card, properties), plc));
            }

            return axes;
        }

        /// <summary>
        /// One TCP/IP device per entry, bound to the library's default PLC array for its
        /// type (ScannerGVL.Scanners[n], VisionGVL.Visions[n], RobotGVL.Robots[n]). n is the entry's position in
        /// its file, starting at 0 as the PLC arrays do. When the PLC is up, each binding is
        /// checked, so a file with more devices than the PLC array is reported at startup.
        /// </summary>
        public static List<INetworkProtocol> CreateTcpDevices(
            TwincatPlcConnection plc,
            NetworkDeviceType type,
            IEnumerable<SocketClientConfiguration> configs,
            ICollection<string> warnings)
        {
            var devices = new List<INetworkProtocol>();
            bool connected = plc.IsConnected;
            int id = 0;

            foreach (var cfg in configs)
            {
                NetworkDeviceProperties properties;
                switch (type)
                {
                    case NetworkDeviceType.Vision: properties = new VisionProperties(); break;
                    case NetworkDeviceType.Robot: properties = new RobotProperties(); break;
                    default: properties = new ScannerProperties(); break;
                }
                properties.Id = id++;
                properties.Name = cfg.Name;
                properties.EnumName = cfg.EnumName;

                var device = TcpDeviceFactory.Create(plc.Card, properties);

                if (connected && !plc.SymbolExists(device.Instance + ".status.connected"))
                {
                    warnings.Add(cfg.EnumName + ": " + device.Instance + " does not exist in the PLC. "
                        + "Its position in the device file is its index in the PLC array, starting at 0.");
                }

                devices.Add(new TwincatTcpDeviceAdapter(device, plc));
            }

            return devices;
        }

        private static List<string> Names(List<string> configured)
        {
            return configured == null
                ? new List<string>()
                : configured.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        }

        private static string Symbol(string ioGvl, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "";

            return string.IsNullOrWhiteSpace(ioGvl) ? name : ioGvl + "." + name;
        }

        private static List<string> Addresses(List<string> names, Func<string, bool, string> resolveAddress)
        {
            return resolveAddress == null
                ? names
                : names.Select(n => resolveAddress(n, false)).ToList();
        }
    }
}
