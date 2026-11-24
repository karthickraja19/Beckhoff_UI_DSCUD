using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Data.Models;

namespace Nova.ViewModels.Pages.Recipe
{
    /// <summary>
    /// A schema-driven dynamic table row supporting arbitrary columns,
    /// dynamic property descriptors, per-column numeric validation,
    /// and row identifier / spare-row detection.
    /// </summary>
    public partial class DynamicGridRow : ObservableObject, INotifyDataErrorInfo, ICustomTypeDescriptor
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyList<DynamicColumnSchema> _columns;

        public IReadOnlyDictionary<string, string> Values => _values;
        public IReadOnlyList<DynamicColumnSchema> Columns => _columns;

        public string RowIdentifier => _columns != null && _columns.Count > 0 && _values.TryGetValue(_columns[0].Key, out var id) ? id : string.Empty;
        public bool IsSpare => RowIdentifier != null && RowIdentifier.StartsWith("Spare", StringComparison.OrdinalIgnoreCase);

        public bool HasErrors => _errors.Count > 0;
        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        public string this[string columnKey]
        {
            get => _values.TryGetValue(columnKey ?? string.Empty, out var val) ? val : string.Empty;
            set
            {
                if (columnKey == null) return;
                string current = _values.TryGetValue(columnKey, out var val) ? val : string.Empty;
                if (!string.Equals(current, value, StringComparison.Ordinal))
                {
                    _values[columnKey] = value ?? string.Empty;
                    ValidateColumn(columnKey, value);
                    OnPropertyChanged("Item[]");
                    OnPropertyChanged("Item[" + columnKey + "]");
                    OnPropertyChanged(nameof(Values));
                    OnPropertyChanged(nameof(RowIdentifier));
                    OnPropertyChanged(nameof(IsSpare));
                }
            }
        }

        public DynamicGridRow(Dictionary<string, string> values, IReadOnlyList<DynamicColumnSchema> columns)
        {
            _columns = columns ?? Array.Empty<DynamicColumnSchema>();
            if (values != null)
            {
                foreach (var kvp in values)
                {
                    _values[kvp.Key] = kvp.Value ?? string.Empty;
                }
            }

            foreach (var col in _columns)
            {
                if (!_values.ContainsKey(col.Key))
                {
                    _values[col.Key] = string.Empty;
                }
            }

            for (int i = 1; i < _columns.Count; i++)
            {
                var col = _columns[i];
                ValidateColumn(col.Key, _values[col.Key]);
            }
        }

        public Dictionary<string, string> ToDictionary()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in _columns)
            {
                result[col.Key] = _values.TryGetValue(col.Key, out var val) ? val : string.Empty;
            }
            return result;
        }

        public IEnumerable GetErrors(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return _errors.SelectMany(kv => kv.Value);
            }

            string cleanProp = propertyName;
            if (cleanProp.StartsWith("Item[") && cleanProp.EndsWith("]"))
            {
                cleanProp = cleanProp.Substring(5, cleanProp.Length - 6);
            }
            else if (cleanProp.StartsWith("[") && cleanProp.EndsWith("]"))
            {
                cleanProp = cleanProp.Substring(1, cleanProp.Length - 2);
            }

            if (_errors.TryGetValue(cleanProp, out var errors))
            {
                return errors;
            }

            return Array.Empty<string>();
        }

        private void ValidateColumn(string columnKey, string value)
        {
            var colSchema = _columns.FirstOrDefault(c => string.Equals(c.Key, columnKey, StringComparison.OrdinalIgnoreCase));
            if (colSchema == null || colSchema.IsReadOnly || colSchema.IsIdentifier || !colSchema.ValidationEnabled)
            {
                ClearErrors(columnKey);
                return;
            }

            // Empty or whitespace cells in sparse/optional rows are valid (no false validation errors)
            if (string.IsNullOrWhiteSpace(value))
            {
                ClearErrors(columnKey);
                return;
            }

            // FAM 2 SET_05: an IP address must be one, not any text.
            if (colSchema.IsIpAddress)
            {
                if (IsIpv4(value))
                    ClearErrors(columnKey);
                else
                    SetErrors(columnKey, "Must be an IP address such as 192.168.1.10.");
                return;
            }

            switch (colSchema.DataType)
            {
                case ColumnDataType.Text:
                    // Text columns accept valid strings ('Odd', 'Even', 'None', 'A', 'B', 'mm', '192.168.1.100')
                    ClearErrors(columnKey);
                    break;

                case ColumnDataType.Numeric:
                    if (!TryParseNumber(value, out _))
                    {
                        SetErrors(columnKey, "Must be a valid numeric value.");
                    }
                    else
                    {
                        ClearErrors(columnKey);
                    }
                    break;

                case ColumnDataType.NonNegative:
                    if (!TryParseNumber(value, out var nonNegVal))
                    {
                        SetErrors(columnKey, "Must be a valid numeric value.");
                    }
                    else if (nonNegVal < 0d)
                    {
                        SetErrors(columnKey, "Must be greater than or equal to 0.");
                    }
                    else
                    {
                        ClearErrors(columnKey);
                    }
                    break;

                case ColumnDataType.Integer:
                    if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal)
                        && !long.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out intVal))
                    {
                        SetErrors(columnKey, "Must be a valid integer.");
                    }
                    else if (intVal < 0)
                    {
                        SetErrors(columnKey, "Must be greater than or equal to 0.");
                    }
                    else
                    {
                        ClearErrors(columnKey);
                    }
                    break;
            }
        }

        /// <summary>Four numbers of 0-255, dot-separated, digits only.</summary>
        private static bool IsIpv4(string value)
        {
            string[] parts = value.Trim().Split('.');
            if (parts.Length != 4)
                return false;

            foreach (string part in parts)
            {
                if (part.Length == 0 || part.Length > 3)
                    return false;

                foreach (char c in part)
                {
                    if (c < '0' || c > '9')
                        return false;
                }

                if (int.Parse(part, CultureInfo.InvariantCulture) > 255)
                    return false;
            }

            return true;
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
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs("Item[" + propertyName + "]"));
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs("[" + propertyName + "]"));
        }

        #region ICustomTypeDescriptor implementation
        public AttributeCollection GetAttributes() => TypeDescriptor.GetAttributes(this, true);
        public string GetClassName() => TypeDescriptor.GetClassName(this, true);
        public string GetComponentName() => TypeDescriptor.GetComponentName(this, true);
        public TypeConverter GetConverter() => TypeDescriptor.GetConverter(this, true);
        public EventDescriptor GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(this, true);
        public PropertyDescriptor GetDefaultProperty() => TypeDescriptor.GetDefaultProperty(this, true);
        public object GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(this, editorBaseType, true);
        public EventDescriptorCollection GetEvents() => TypeDescriptor.GetEvents(this, true);
        public EventDescriptorCollection GetEvents(Attribute[] attributes) => TypeDescriptor.GetEvents(this, attributes, true);
        public PropertyDescriptorCollection GetProperties() => GetProperties(null);
        public PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            var propDescriptors = new List<PropertyDescriptor>();
            foreach (var col in _columns)
            {
                propDescriptors.Add(new DynamicColumnPropertyDescriptor(col.Key, typeof(string), col.IsReadOnly));
            }
            return new PropertyDescriptorCollection(propDescriptors.ToArray());
        }
        public object GetPropertyOwner(PropertyDescriptor pd) => this;
        #endregion
    }

    public sealed class DynamicColumnPropertyDescriptor : PropertyDescriptor
    {
        private readonly bool _isReadOnly;

        public DynamicColumnPropertyDescriptor(string name, Type type, bool isReadOnly)
            : base(name, null)
        {
            _isReadOnly = isReadOnly;
        }

        public override Type ComponentType => typeof(DynamicGridRow);
        public override bool IsReadOnly => _isReadOnly;
        public override Type PropertyType => typeof(string);
        public override bool CanResetValue(object component) => false;
        public override object GetValue(object component)
        {
            if (component is DynamicGridRow row)
            {
                return row[Name];
            }
            return null;
        }
        public override void ResetValue(object component) { }
        public override void SetValue(object component, object value)
        {
            if (component is DynamicGridRow row)
            {
                row[Name] = value?.ToString() ?? string.Empty;
            }
        }
        public override bool ShouldSerializeValue(object component) => false;
    }
}
