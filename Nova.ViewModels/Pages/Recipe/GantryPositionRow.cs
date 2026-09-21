using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Recipe
{
    /// <summary>
    /// One taught position in the Gantry recipe table (V3 dgv_PickGantry /
    /// *Receipe_Param.csv: Position, then X/Y/Z each with Vel, Acc and De-Acc).
    /// </summary>
    public partial class GantryPositionRow : ObservableObject, INotifyDataErrorInfo
    {
        private readonly Dictionary<string, List<string>> _errors = new Dictionary<string, List<string>>();

        public string Position { get; }

        [ObservableProperty]
        private string _x;

        [ObservableProperty]
        private string _xVel;

        [ObservableProperty]
        private string _xAcc;

        [ObservableProperty]
        private string _xDeAcc;

        [ObservableProperty]
        private string _y;

        [ObservableProperty]
        private string _yVel;

        [ObservableProperty]
        private string _yAcc;

        [ObservableProperty]
        private string _yDeAcc;

        [ObservableProperty]
        private string _z;

        [ObservableProperty]
        private string _zVel;

        [ObservableProperty]
        private string _zAcc;

        [ObservableProperty]
        private string _zDeAcc;

        public bool HasErrors => _errors.Count > 0;

        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        public GantryPositionRow(string position, string x = "0", string y = "0", string z = "0")
        {
            Position = position;
            _x = x;
            _y = y;
            _z = z;
            _xVel = _xAcc = _xDeAcc = "0";
            _yVel = _yAcc = _yDeAcc = "0";
            _zVel = _zAcc = _zDeAcc = "0";
            ValidateAll();
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

        partial void OnXChanged(string value) => ValidatePosition(nameof(X), value);
        partial void OnYChanged(string value) => ValidatePosition(nameof(Y), value);
        partial void OnZChanged(string value) => ValidatePosition(nameof(Z), value);

        partial void OnXVelChanged(string value) => ValidateMotion(nameof(XVel), value, 5000d);
        partial void OnYVelChanged(string value) => ValidateMotion(nameof(YVel), value, 5000d);
        partial void OnZVelChanged(string value) => ValidateMotion(nameof(ZVel), value, 5000d);

        partial void OnXAccChanged(string value) => ValidateMotion(nameof(XAcc), value, 7000d);
        partial void OnYAccChanged(string value) => ValidateMotion(nameof(YAcc), value, 7000d);
        partial void OnZAccChanged(string value) => ValidateMotion(nameof(ZAcc), value, 7000d);

        partial void OnXDeAccChanged(string value) => ValidateMotion(nameof(XDeAcc), value, 7000d);
        partial void OnYDeAccChanged(string value) => ValidateMotion(nameof(YDeAcc), value, 7000d);
        partial void OnZDeAccChanged(string value) => ValidateMotion(nameof(ZDeAcc), value, 7000d);

        private void ValidateAll()
        {
            ValidatePosition(nameof(X), X);
            ValidatePosition(nameof(Y), Y);
            ValidatePosition(nameof(Z), Z);
            ValidateMotion(nameof(XVel), XVel, 5000d);
            ValidateMotion(nameof(YVel), YVel, 5000d);
            ValidateMotion(nameof(ZVel), ZVel, 5000d);
            ValidateMotion(nameof(XAcc), XAcc, 7000d);
            ValidateMotion(nameof(YAcc), YAcc, 7000d);
            ValidateMotion(nameof(ZAcc), ZAcc, 7000d);
            ValidateMotion(nameof(XDeAcc), XDeAcc, 7000d);
            ValidateMotion(nameof(YDeAcc), YDeAcc, 7000d);
            ValidateMotion(nameof(ZDeAcc), ZDeAcc, 7000d);
        }

        private void ValidatePosition(string propertyName, string value)
        {
            if (TryParseNumber(value, out _))
            {
                ClearErrors(propertyName);
                return;
            }

            SetErrors(propertyName, "Must be a valid numeric value.");
        }

        private void ValidateMotion(string propertyName, string value, double maxValue)
        {
            if (!TryParseNumber(value, out var numericValue))
            {
                SetErrors(propertyName, "Must be a valid numeric value.");
                return;
            }

            if (numericValue < 0d)
            {
                SetErrors(propertyName, "Must be greater than or equal to 0.");
                return;
            }

            if (numericValue > maxValue)
            {
                SetErrors(propertyName, "Must be less than or equal to " + maxValue.ToString("0", CultureInfo.InvariantCulture) + ".");
                return;
            }

            ClearErrors(propertyName);
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
