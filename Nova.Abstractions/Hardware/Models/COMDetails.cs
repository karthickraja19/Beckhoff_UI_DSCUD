using System.IO.Ports;

namespace Nova.Abstractions.Hardware.Models
{
    public class COMDetails
    {
        public string COMName { get; set; } = string.Empty;
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public Parity Parity { get; set; }
        public StopBits StopBits { get; set; }
        public Handshake HandShake { get; set; }
    }
}
