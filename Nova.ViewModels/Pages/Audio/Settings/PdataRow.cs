using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// One selected PDATA field in the grid. Placeholder, Prefix, Datatype and Suffix are
    /// derived and shown read-only; Suffix is the live value and is only filled in when a
    /// unit is reported. Min and Max limits must be numbers (or empty).
    /// </summary>
    public partial class PdataRow : ObservableObject, IDataErrorInfo
    {
        public PdataRow(string placeholder, string datatype, string prefix, string suffix, string minLimit, string maxLimit, string unit)
        {
            Placeholder = placeholder ?? string.Empty;
            Datatype = datatype ?? string.Empty;
            Prefix = prefix ?? string.Empty;
            Suffix = suffix ?? string.Empty;
            _minLimit = minLimit ?? string.Empty;
            _maxLimit = maxLimit ?? string.Empty;
            _unit = unit ?? string.Empty;
        }

        public string Placeholder { get; }
        public string Datatype { get; }
        public string Prefix { get; }
        public string Suffix { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasErrors))]
        private string _minLimit;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasErrors))]
        private string _maxLimit;

        [ObservableProperty]
        private string _unit;

        public bool HasErrors => !string.IsNullOrEmpty(this[nameof(MinLimit)]) || !string.IsNullOrEmpty(this[nameof(MaxLimit)]);

        public string Error => null;

        public string this[string columnName]
        {
            get
            {
                string value = columnName == nameof(MinLimit) ? MinLimit
                             : columnName == nameof(MaxLimit) ? MaxLimit
                             : null;

                if (string.IsNullOrWhiteSpace(value))
                    return null;

                double parsed;
                return double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                    ? null
                    : "Enter a number.";
            }
        }
    }
}
