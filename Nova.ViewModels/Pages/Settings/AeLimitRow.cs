using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>One AE-limit parameter row: name + editable min/max/unit (blank shows as "—").</summary>
    public partial class AeLimitRow : ObservableObject
    {
        public int SerialNo { get; }
        public string Parameter { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MinDisplay))]
        private string _min;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MaxDisplay))]
        private string _max;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UnitDisplay))]
        private string _unit;

        public string MinDisplay => string.IsNullOrWhiteSpace(Min) ? "—" : Min;
        public string MaxDisplay => string.IsNullOrWhiteSpace(Max) ? "—" : Max;
        public string UnitDisplay => string.IsNullOrWhiteSpace(Unit) ? "—" : Unit;

        public AeLimitRow(int serialNo, string parameter, string min = "", string max = "", string unit = "")
        {
            SerialNo = serialNo;
            Parameter = parameter;
            _min = min;
            _max = max;
            _unit = unit;
        }
    }

    /// <summary>A page-number chip in the AE-limits pager.</summary>
    public class AePageItem
    {
        public int Number { get; }
        public bool IsCurrent { get; }

        public AePageItem(int number, bool isCurrent)
        {
            Number = number;
            IsCurrent = isCurrent;
        }
    }
}
