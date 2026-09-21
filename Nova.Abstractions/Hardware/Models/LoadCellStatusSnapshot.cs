namespace Nova.Abstractions.Hardware
{
    /// <summary>
    /// Point-in-time read-back of a loadcell channel (value in kg).
    /// </summary>
    public class LoadCellStatusSnapshot
    {
        public string ComponentId { get; set; }

        public double Value { get; set; }
    }
}
