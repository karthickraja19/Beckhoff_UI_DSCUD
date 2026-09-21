namespace Nova.Abstractions.Data.Models
{
    public enum AeLimitKind
    {
        Production = 0,
        Cpk = 1,
        Grr = 2,
    }

    public sealed class AxisParameterRecord
    {
        public string CardId { get; set; } = "0";
        public string AxisId { get; set; } = "0";
        public string AxisName { get; set; } = string.Empty;
        public string Pitch { get; set; } = "0";
        public string Pulses { get; set; } = "0";
        public string Ppr { get; set; } = "0";
        public string HomingAcc { get; set; } = "0";
        public string HomingDeAcc { get; set; } = "0";
        public string HomingSpeed { get; set; } = "0";
    }

    public sealed class TcpConnectionRecord
    {
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Port { get; set; } = string.Empty;
    }

    public sealed class SerialPortRecord
    {
        public string Name { get; set; } = string.Empty;
        public string BaudRate { get; set; } = string.Empty;
        public string DataBits { get; set; } = string.Empty;
        public string Parity { get; set; } = string.Empty;
        public string StopBits { get; set; } = string.Empty;
        public string HandShake { get; set; } = string.Empty;
    }

    public sealed class VisionParameterRecord
    {
        public string Colour { get; set; } = string.Empty;
        public string OptId { get; set; } = string.Empty;
        public string CameraId { get; set; } = string.Empty;
    }

    public sealed class AeLimitRecord
    {
        public int SerialNo { get; set; }
        public string Parameter { get; set; } = string.Empty;
        public string Min { get; set; } = string.Empty;
        public string Max { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
    }
}
