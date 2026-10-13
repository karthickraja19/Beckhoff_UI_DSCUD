namespace Master.Configuration.Model
{
    /// <summary>
    /// One entry of Steppers.json: the library's StepperProperties, property for property. Only
    /// I/O steppers are listed there; a stepper driven as an NC axis is a servo (Servos.json).
    /// </summary>
    public class StepperConfiguration
    {
        public string EnumName { get; set; }
        public string Name { get; set; }

        /// <summary>The stepper's index in {DBName}.{StepperDUTName}.stepperdata[] - the PLC array starts at 1.</summary>
        public int Id { get; set; }
    }
}
