using Master.Configuration.Model;
using System.Collections.Generic;
using Teal.Library.Contracts;
using Teal.Library.Servo;
using Teal.Library.Twincat;

namespace Master.Configuration
{
    /// <summary>
    /// The library servo axis for each member of <see cref="Servos"/> configured in
    /// Data\Servos.json. Created by <see cref="DeviceObjects.Create"/>.
    /// </summary>
    public static class ServoObjects
    {
        public static readonly Dictionary<Servos, AxisServo> ServoMotorObj = new Dictionary<Servos, AxisServo>();

        internal static void Load(TwincatCard card, List<string> problems)
        {
            ServoMotorObj.Clear();

            foreach (var servo in MachineConfiguration.Configured<Servos, ServoConfiguration>(ConfigGlobalClass.ServoConfigurations, c => c.EnumName))
            {
                ServoConfiguration config = servo.Value;
                DeviceObjects.Add(ServoMotorObj, servo.Key, () =>
                {
                    var properties = new AxisProperties
                    {
                        Id = config.Id,
                        Name = config.Name,
                        EnumName = config.EnumName,
                        Type = config.Type,
                        Pulse = config.Pulse,
                        Velocity = config.Velocity,
                        Acceleration = config.Acceleration,
                        Deceleration = config.Deceleration,
                        PositiveLimit = config.PositiveLimit,
                        NegativeLimit = config.NegativeLimit,
                        Timeout = config.Timeout,
                    };

                    return new AxisServo(card, properties);
                }, problems);
            }
        }
    }
}
