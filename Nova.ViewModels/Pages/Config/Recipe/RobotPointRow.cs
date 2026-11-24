using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Recipe
{
    /// <summary>
    /// One parameter in the Robot recipe point table (V3 dgv_Robot /
    /// RoboReceipe_Param.csv: PointName, Value). Spare rows render dimmed.
    /// </summary>
    public partial class RobotPointRow : ObservableObject, INotifyDataErrorInfo
    {
        private readonly Dictionary<string, List<string>> _errors = new Dictionary<string, List<string>>();

        public string PointName { get; }
        public bool IsSpare { get; }

        [ObservableProperty]
        private string _value;

        public bool HasErrors => _errors.Count > 0;

        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        public RobotPointRow(string pointName, string value, bool isSpare = false)
        {
            PointName = pointName;
            IsSpare = isSpare;
            _value = value;
            ValidateValue(value);
        }

        public IEnumerable GetErrors(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return Array.Empty<string>();
            }

            if (_errors.TryGetValue(propertyName, out var errors))
            {
                return errors;
            }

            return Array.Empty<string>();
        }

        partial void OnValueChanged(string value) => ValidateValue(value);

        private void ValidateValue(string value)
        {
            if (!TryParseNumber(value, out var numericValue))
            {
                SetErrors(nameof(Value), "Must be a valid numeric value.");
                return;
            }

            if (numericValue < 0d)
            {
                SetErrors(nameof(Value), "Must be greater than or equal to 0.");
                return;
            }

            ClearErrors(nameof(Value));
        }

        private static bool TryParseNumber(string value, out double parsed)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed);
        }

        private void SetErrors(string propertyName, string error)
        {
            _errors[propertyName] = new List<string> { error };
            RaiseErrorsChanged(propertyName);
        }

        private void ClearErrors(string propertyName)
        {
            if (_errors.Remove(propertyName))
            {
                RaiseErrorsChanged(propertyName);
            }
        }

        private void RaiseErrorsChanged(string propertyName)
        {
            OnPropertyChanged(nameof(HasErrors));
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        }
    }
}
