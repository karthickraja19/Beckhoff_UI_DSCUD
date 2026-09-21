namespace Nova.Abstractions.Hardware.Models
{
    public enum RobotMode
    {
        Auto,
        Teach,
        Calibration,
        Recipe,
        Initialization
    }

    public class RobotResponse
    {
        public bool IsSuccess { get; set; }
        public string RawResponse { get; set; }
        public string ErrorMessage { get; set; }
        public string[] ParsedData { get; set; }

        public static RobotResponse Success(string raw, string[] parsed = null)
        {
            return new RobotResponse { IsSuccess = true, RawResponse = raw, ParsedData = parsed };
        }

        public static RobotResponse Failure(string error, string raw = null)
        {
            return new RobotResponse { IsSuccess = false, ErrorMessage = error, RawResponse = raw };
        }
    }
}
