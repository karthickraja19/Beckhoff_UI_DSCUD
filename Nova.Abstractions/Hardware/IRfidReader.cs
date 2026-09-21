using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    public interface IRfidReader
    {
        string TryReadRFIDCard();
        Task<string> TryReadRFIDCardAsync();
        void WriteToRFID(string txtFpc);
    }
}
