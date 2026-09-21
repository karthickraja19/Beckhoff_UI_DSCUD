using Master.Configuration.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using Teal.Library.Contracts;
using Teal.Library.Servo;

namespace Master.Configuration
{
    public static class ServoObjects
    {
        public static Dictionary<Servos, IAxis> ServoMotorObj = new Dictionary<Servos, IAxis>();
        
        public static void LoadServos()
        {
            var servoJsonString = File.ReadAllText(Application.StartupPath + "\\Data\\Servos.json");

            ConfigGlobalClass.ServoConfigurations = JsonConvert.DeserializeObject<List<ServoConfiguration>>(servoJsonString);

            foreach (var servoEnum in Enum.GetValues(typeof(Servos)).Cast<Servos>())
            {
                string servoEnumName = servoEnum.ToString();

                var servoConfig = ConfigGlobalClass.ServoConfigurations.Where(x => x.EnumName == servoEnumName).FirstOrDefault();

                try
                {
                    AxisProperties axisProperties = new AxisProperties
                    {
                        Id = servoConfig.Id,
                        Name = servoConfig.Name,
                        Pulse = servoConfig.Pulse,
                        Velocity = servoConfig.Speed,
                        Acceleration = servoConfig.Acceleration,
                        Deceleration = servoConfig.Deceleration,
                        NegativeLimit = servoConfig.SoftLimits.Negative,
                        PositiveLimit = servoConfig.SoftLimits.Positive,
                        Type = MotorType.Servo,
                    };

                    IAxis axis = new AxisServo(ConfigGlobalClass.cardManager.Card, axisProperties);
                    ServoMotorObj[servoEnum] = axis;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing config for {servoEnumName}: {ex.Message}");
                    MessageBox.Show($"Error parsing config for {servoEnumName}: {ex.Message}");
                }
            }
        }
    }
}
