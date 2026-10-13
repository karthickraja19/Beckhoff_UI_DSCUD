using Nova.Abstractions.Hardware;

namespace Nova.Wpf.App.Services.Twincat.HmiDisplay
{
    /// <summary>
    /// A vacuum's HMI display struct - what the PLC publishes for the vacuum board. Every
    /// struct App.config can name for it (VacuumHmiDisplayType) implements this.
    /// </summary>
    public interface IVacuumHmiDisplay
    {
        /// <summary>Copies what the PLC published into the snapshot the board shows.</summary>
        void Fill(VacuumStatusSnapshot snapshot);
    }
}
