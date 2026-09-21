using Master.Configuration.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using Teal.Library.Contracts;
using Teal.Library.Stepper;

namespace Master.Configuration
{
    public static class StepperObjects
    {
        public static Dictionary<Steppers, IStepper> StepperMotorObj = new Dictionary<Steppers, IStepper>();

        public static void LoadSteppers()
        {
            var stepperJsonString = File.ReadAllText(Application.StartupPath + "\\Data\\Steppers.json");

            ConfigGlobalClass.StepperConfigurations = JsonConvert.DeserializeObject<List<StepperConfiguration>>(stepperJsonString);

            foreach (var stepperEnum in Enum.GetValues(typeof(Steppers)).Cast<Steppers>())
            {
                string stepperName = stepperEnum.ToString();

                var stepperConfig = ConfigGlobalClass.StepperConfigurations.Where(x => x.EnumName == stepperName).FirstOrDefault();

                try
                {
                    IStepper stepper = null;
                    if (stepperConfig.IsAxisEnabled)
                    {
                        StepperProperties stepperProperties = new StepperProperties
                        {
                            Name = stepperConfig.Name,
                            Id = stepperConfig.Id,
                            Pitch = stepperConfig.Pitch,
                            Pulse = stepperConfig.Pulse,
                            PPR = stepperConfig.PPR,
                            Speed = stepperConfig.Speed,
                            Acceleration = stepperConfig.Acceleration,
                            Deacceleration = stepperConfig.Deceleration
                        };
                        stepper = new AxisStepper(ConfigGlobalClass.cardManager.Card, stepperProperties);
                    }
                    else
                    {
                        StepperProperties stepperProperties = new StepperProperties
                        {
                            Name = stepperConfig.Name,
                            ForwardCommand = (int)GetEnumValue<EOutput>(stepperConfig.ForwardCommand),
                            ReverseCommand = (int)GetEnumValue<EOutput>(stepperConfig.ReverseCommand),
                            AlarmSignal = (int)GetEnumValue<EInput>(stepperConfig.AlarmSignal),
                            AlarmResetCommand = (int)GetEnumValue<EOutput>(stepperConfig.AlarmResetCommand),
                        };
                        stepper = new IOStepper(ConfigGlobalClass.cardManager.Card, stepperProperties);
                    }

                    StepperMotorObj[stepperEnum] = stepper;
                }

                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing config for {stepperName}: {ex.Message}");
                    MessageBox.Show($"Error parsing config for {stepperName}: {ex.Message}");
                }
            }
        }

        private static T GetEnumValue<T>(string value) where T : struct, Enum
        {
            Enum.TryParse<T>(value, true, out var enumValue);

            return enumValue;
        }
    }
}
