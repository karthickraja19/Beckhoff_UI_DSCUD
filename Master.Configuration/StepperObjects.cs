using Master.Configuration.Model;
using System.Collections.Generic;
using Teal.Library.Contracts;
using Teal.Library.Stepper;
using Teal.Library.Twincat;

namespace Master.Configuration
{
    /// <summary>
    /// The library stepper for each member of <see cref="Steppers"/> configured in
    /// Data\Steppers.json. Created by <see cref="DeviceObjects.Create"/>.
    /// </summary>
    public static class StepperObjects
    {
        public static readonly Dictionary<Steppers, IStepper> StepperMotorObj = new Dictionary<Steppers, IStepper>();

        /// <summary>Every stepper is an I/O stepper (IOStepper, on the Stepper tab).</summary>
        internal static void Load(TwincatCard card, List<string> problems)
        {
            StepperMotorObj.Clear();

            foreach (var stepper in MachineConfiguration.Configured<Steppers, StepperConfiguration>(ConfigGlobalClass.StepperConfigurations, c => c.EnumName))
            {
                StepperConfiguration config = stepper.Value;
                DeviceObjects.Add(StepperMotorObj, stepper.Key, () =>
                {
                    var properties = new StepperProperties
                    {
                        Name = config.Name,
                        EnumName = config.EnumName,
                        Id = config.Id,
                    };

                    return new IOStepper(card, properties);
                }, problems);
            }
        }
    }
}
