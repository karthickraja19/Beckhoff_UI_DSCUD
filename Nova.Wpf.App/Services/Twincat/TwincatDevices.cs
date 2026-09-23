using System;
using System.Collections.Generic;
using System.Linq;
using Nova.Core.Config;
using Teal.Library.Contracts;
using Teal.Library.Stepper;
using NovaCylinder = Nova.Abstractions.Hardware.ICylinder;
using NovaStepper = Nova.Abstractions.Hardware.IStepper;
using TealCylinder = Teal.Library.Cylinder.Cylinder;
using TealStepper = Teal.Library.Contracts.IStepper;

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
