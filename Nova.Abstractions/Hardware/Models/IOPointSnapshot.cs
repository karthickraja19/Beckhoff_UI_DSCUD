namespace Nova.Abstractions.Hardware
{
    /// <summary>
    /// One digital IO point (input or output) as defined by the machine's
    /// Data\Config.csv row: the bit number within its bank, the logical name the
    /// engine uses, the wiring address label (e.g. "X0-05" / "Y0-03"), and its
    /// last-read state.
    /// </summary>
    public class IOPointSnapshot
    {
        public int Bit { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public bool IsOn { get; set; }
    }
}
