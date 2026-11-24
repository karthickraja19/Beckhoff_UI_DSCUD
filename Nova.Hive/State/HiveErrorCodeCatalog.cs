using Nova.Abstractions.Services;

namespace Nova.Hive
{
    /// <summary>
    /// <see cref="IErrorCodeCatalog"/> over <see cref="HiveErrorCodes"/>: the Alarm page
    /// shows the code and error type HIVE was given, and the machine's own text as detail.
    /// </summary>
    public sealed class HiveErrorCodeCatalog : IErrorCodeCatalog
    {
        private readonly HiveErrorCodes _codes;

        public HiveErrorCodeCatalog(string csvFile)
        {
            _codes = HiveErrorCodes.Load(csvFile);
        }

        public DfmError Translate(string plcCode, string plcDetail)
        {
            HiveError known = _codes.Lookup(plcCode);
            HiveError error = known ?? HiveErrorCodes.Others(plcDetail);

            return new DfmError
            {
                Code = error.Code,
                Message = error.Message,
                // Slide 56: "Show original errors reported by machine".
                Detail = string.IsNullOrWhiteSpace(plcDetail) ? error.Detail : plcDetail,
                IsMapped = known != null,
            };
        }
    }
}
