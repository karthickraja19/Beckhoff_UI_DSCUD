namespace Master.Configuration.Model
{
    /// <summary>A position taught on a servo (Servos.json ConfiguredPositions): a row of its gantry module's recipe.</summary>
    public class AxisPosition
    {
        public string Name { get; set; }
        public double Value { get; set; }
        public double Speed { get; set; }
        public double Acceleration { get; set; }
        public double Deceleration { get; set; }
    }
}
