using System.Collections.Generic;
using Nova.Core.Config;
using Teal.Library.Contracts;
using Teal.Library.Stepper;
using NovaStepper = Nova.Abstractions.Hardware.IStepper;
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
    }
}
