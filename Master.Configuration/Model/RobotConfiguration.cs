namespace Master.Configuration.Model
{
    /// <summary>
    /// One entry of Robots.json: the library's RobotProperties, property for property. Id is the
    /// robot's number in the PLC - Robot{Id}_hw_IO_GVL for its EtherCAT I/O and
    /// RobotGVL.Robots[Id] for FB_RobotSD - starting at 1.
    /// </summary>
    public class RobotConfiguration
    {
        public string EnumName { get; set; }
        public string Name { get; set; }
        public int Id { get; set; }
        public string StationName { get; set; }
    }
}
