using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>A station the Calibration tab can calibrate.</summary>
    public sealed class CalibrationStationOption
    {
        public CalibrationStationOption(int number, string name)
        {
            Number = number;
            Name = name;
        }

        /// <summary>The PLC's nStation.</summary>
        public int Number { get; }

        public string Name { get; }

        public override string ToString() => Name;
    }

    /// <summary>One parameter on the Calibration tab: the operator's text, checked against the routine's range.</summary>
    public sealed class CalibrationParameterRow : ObservableObject
    {
        private readonly Action _edited;
        private string _text;
        private string _error = string.Empty;
        private bool _loading;

        public CalibrationParameterRow(CalibrationParameter definition, Action edited)
        {
            Definition = definition;
            _edited = edited;
        }

        public CalibrationParameter Definition { get; }

        public string Label => Definition.Label;

        public string Unit => Definition.Unit;

        public string RangeText => Format(Definition.Min) + " to " + Format(Definition.Max)
            + (Definition.Unit.Length > 0 ? " " + Definition.Unit : string.Empty);

        public string Text
        {
            get { return _text; }
            set
            {
                if (!SetProperty(ref _text, value))
                    return;

                Validate();
                if (!_loading)
                    _edited?.Invoke();
            }
        }

        /// <summary>Why the text is not a usable value; empty when it is.</summary>
        public string Error
        {
            get { return _error; }
            private set
            {
                if (SetProperty(ref _error, value))
                    OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => Error.Length > 0;

        /// <summary>The value, or null while the text is empty or not a number in range.</summary>
        public double? Value { get; private set; }

        /// <summary>Shows a value without counting it as an edit.</summary>
        public void Load(double? value)
        {
            _loading = true;
            try
            {
                Text = value.HasValue ? Format(value.Value) : string.Empty;
                Validate();
            }
            finally
            {
                _loading = false;
            }
        }

        private void Validate()
        {
            Value = null;
            string text = (_text ?? string.Empty).Trim();
            double value;
            if (text.Length == 0)
            {
                Error = "Required";
            }
            else if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                     && !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                Error = "Not a number";
            }
            else if (value < Definition.Min || value > Definition.Max)
            {
                Error = "Out of range (" + RangeText + ")";
            }
            else
            {
                Value = value;
                Error = string.Empty;
            }
        }

        private string Format(double value)
        {
            return value.ToString("0." + new string('#', Math.Max(Definition.Decimals, 1)), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>One cell of the result table.</summary>
    public sealed class CalibrationResultCell
    {
        public CalibrationResultCell(string text, bool isFail)
        {
            Text = text;
            IsFail = isFail;
        }

        public string Text { get; }

        public bool IsFail { get; }
    }

    /// <summary>One row of the result table: its label and a cell per column.</summary>
    public sealed class CalibrationResultRow
    {
        public CalibrationResultRow(string label, IReadOnlyList<CalibrationResultCell> cells)
        {
            Label = label;
            Cells = cells;
        }

        public string Label { get; }

        public IReadOnlyList<CalibrationResultCell> Cells { get; }
    }
}
