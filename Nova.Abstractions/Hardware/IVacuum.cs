using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    /// <summary>
    /// One vacuum unit as the Manual screen drives it. Vacuum/Blow/Off mirror V3's
    /// manual-mode buttons (fire the output, confirm feedback with the configured
    /// timeout); no auto-cycle logic. <see cref="ReadStatus"/> is polled off the IO thread.
    /// </summary>
    public interface IVacuum
    {
        string ComponentId { get; }
        string VacuumName { get; }
        string FeedbackAddress { get; }

        Task<bool> VacuumOnAsync();
        Task<bool> BlowOnAsync();
        Task<bool> OffAsync();

        VacuumStatusSnapshot ReadStatus();
    }
}
