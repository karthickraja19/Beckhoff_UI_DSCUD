using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Data.Models;

namespace Nova.ViewModels.Pages.Recipe
{
    /// <summary>
    /// One axis of the selected CNC channel on the CNC recipe tab: the servo it is mapped
    /// to and its live position. The taught values are the recipe table's columns.
    /// </summary>
    public partial class CncAxisItem : ObservableObject
    {
        public CncAxisItem(string name)
        {
            Name = name;
            IsLinear = !CncChannelRecipe.IsRotational(name);
        }

        /// <summary>One of <see cref="CncChannelRecipe.AxisNames"/>.</summary>
        public string Name { get; }

        /// <summary>True for X1, Y1, Z1, X2, Z2 (mm); false for the rotational A and R (degrees).</summary>
        public bool IsLinear { get; }

        public string Unit => IsLinear ? "mm" : "deg";

        /// <summary>The servo's ComponentId as configured for the channel; blank when not mapped.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ServoText))]
        private string _servo = string.Empty;

        /// <summary>True when <see cref="Servo"/> is one of the machine's axes: only then can it jog or teach.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ServoText))]
        private bool _isMapped;

        public string ServoText => IsMapped
            ? Servo
            : string.IsNullOrWhiteSpace(Servo) ? "not mapped" : Servo + " not found";

        [ObservableProperty]
        private double _livePosition;
    }
}
