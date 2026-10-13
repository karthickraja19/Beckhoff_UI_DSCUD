using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// One robot I/O point. A Bool renders <see cref="IsOn"/> - a read-only indicator for an
    /// input, a toggle switch for an output; a Byte or Word renders <see cref="Value"/>, read only.
    /// </summary>
    public partial class RobotSignal : ObservableObject
    {
        public string Name { get; }
        public RobotIOKind Kind { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsOn))]
        private int _value;

        public bool IsOn => Value != 0;

        public RobotSignal(string name, RobotIOKind kind, int value = 0)
        {
            Name = name;
            Kind = kind;
            _value = value;
        }
    }
}
