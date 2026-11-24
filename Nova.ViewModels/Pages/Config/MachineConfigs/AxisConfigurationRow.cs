using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>
    /// One axis in the Axis Parameters table (V3 Axis_Settings grid / Axis_Parameters.xml:
    /// Card_ID, Axis_ID, Axis_Name, Pitch (mm), Pulses/mm, PPR, Homing Acc/DeAcc/Speed).
    /// </summary>
    public partial class AxisConfigurationRow : ObservableObject
    {
        public string CardId { get; }
        public string AxisId { get; }
        public string AxisName { get; }

        [ObservableProperty]
        private string _pitch;

        [ObservableProperty]
        private string _pulsesPerMm;

        [ObservableProperty]
        private string _ppr;

        [ObservableProperty]
        private string _homingAcc;

        [ObservableProperty]
        private string _homingDeAcc;

        [ObservableProperty]
        private string _homingSpeed;

        public AxisConfigurationRow(string cardId, string axisId, string axisName,
            string pitch, string pulsesPerMm, string ppr,
            string homingAcc, string homingDeAcc, string homingSpeed)
        {
            CardId = cardId;
            AxisId = axisId;
            AxisName = axisName;
            _pitch = pitch;
            _pulsesPerMm = pulsesPerMm;
            _ppr = ppr;
            _homingAcc = homingAcc;
            _homingDeAcc = homingDeAcc;
            _homingSpeed = homingSpeed;
        }
    }
}
