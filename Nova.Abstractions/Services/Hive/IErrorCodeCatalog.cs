namespace Nova.Abstractions.Services
{
    /// <summary>One machine alarm as the DFM names it (slides 30-35).</summary>
    public sealed class DfmError
    {
        /// <summary>The DFM error code, e.g. "M03CVSO-01".</summary>
        public string Code { get; set; }

        /// <summary>The DFM error type, e.g. "Motion Error".</summary>
        public string Message { get; set; }

        /// <summary>The approved error detail, or the machine's own text.</summary>
        public string Detail { get; set; }

        /// <summary>False when the PLC code has no line in the error code file (reported as Others).</summary>
        public bool IsMapped { get; set; }
    }

    /// <summary>
    /// Translates the PLC's alarm codes into the DFM's (Data\HiveErrorCodes.csv), so the
    /// Alarm page shows "Follow error code definition" / "Follow error type definition"
    /// (slide 56) - the same code HIVE was given.
    /// </summary>
    public interface IErrorCodeCatalog
    {
        DfmError Translate(string plcCode, string plcDetail);
    }
}
