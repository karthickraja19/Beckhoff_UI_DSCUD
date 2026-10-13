using Nova.Abstractions.Hardware;

namespace Nova.Wpf.App.Services.Twincat.HmiDisplay
{
    /// <summary>
    /// A servo's or stepper's HMI display struct - what the PLC publishes for the Axis and
    /// Stepper tabs. Every struct App.config can name for them (ServoHmiDisplayType,
    /// StepperHmiDisplayType) implements this.
    /// </summary>
    public interface IAxisHmiDisplay
    {
        /// <summary>Copies what the PLC published into the snapshot the screens show.</summary>
        void Fill(AxisStatusSnapshot snapshot);
    }
}
