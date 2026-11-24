using Master.Configuration.Model;
using System.Collections.Generic;
using Teal.Library.Contracts;
using Teal.Library.Twincat;

namespace Master.Configuration
{
    /// <summary>
    /// The library robot for each member of the <see cref="Master.Configuration.Robots"/>
    /// enum configured in Data\Robots.json. Created by <see cref="DeviceObjects.Create"/>.
    /// </summary>
    public static class RobotObjects
    {
        public static readonly Dictionary<Robots, TwincatRobot> Robots = new Dictionary<Robots, TwincatRobot>();

        /// <summary>
        /// The robots are on EtherCAT: each one's I/O is Robot{Id}_hw_IO_GVL in the PLC, and
        /// FB_RobotSD runs it as RobotGVL.Robots[Id]. The PLC arrays start at 1.
        /// </summary>
        internal static void Load(TwincatCard card, List<string> problems)
        {
            Robots.Clear();

            foreach (var robot in MachineConfiguration.Configured<Master.Configuration.Robots, RobotConfiguration>(ConfigGlobalClass.RobotConfigurations, c => c.EnumName))
            {
                var properties = new RobotProperties
                {
                    Id = robot.Value.Id,
                    Name = robot.Value.Name,
                    EnumName = robot.Value.EnumName,
                    StationName = robot.Value.StationName,
                };
                DeviceObjects.Add(Robots, robot.Key, () => new TwincatRobot(card, properties), problems);
            }
        }
    }
}
