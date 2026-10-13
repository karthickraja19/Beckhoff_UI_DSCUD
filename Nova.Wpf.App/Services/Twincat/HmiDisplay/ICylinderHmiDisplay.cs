using Nova.Abstractions.Hardware;

namespace Nova.Wpf.App.Services.Twincat.HmiDisplay
{
    /// <summary>
    /// A cylinder's HMI display struct - what the PLC publishes for the cylinder board. Every
    /// struct App.config can name for it (CylinderHmiDisplayType) implements this.
    /// </summary>
    public interface ICylinderHmiDisplay
    {
        /// <summary>Copies what the PLC published into the snapshot the board shows.</summary>
        void Fill(CylinderStatusSnapshot snapshot);
    }
}
