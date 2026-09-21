using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>How a <see cref="SettingsField"/> is edited/rendered.</summary>
    public enum SettingsFieldKind
    {
        Text,       // editable text value
        Toggle,     // on/off switch
        ReadOnly    // display-only value (build info, live status, ...)
    }

    /// <summary>
    /// One row in a settings section: a labelled value rendered as text, a toggle,
    /// or a read-only display depending on <see cref="Kind"/>.
    /// </summary>
    public partial class SettingsField : ObservableObject
    {
        public string Label { get; }
        public string Unit { get; }
        public SettingsFieldKind Kind { get; }

        [ObservableProperty]
        private string _value;

        [ObservableProperty]
        private bool _isOn;

        public SettingsField(string label, string value = "", SettingsFieldKind kind = SettingsFieldKind.Text,
            string unit = "", bool isOn = false)
        {
            Label = label;
            _value = value;
            Kind = kind;
            Unit = unit;
            _isOn = isOn;
        }

        public static SettingsField Toggle(string label, bool isOn = false) =>
            new SettingsField(label, kind: SettingsFieldKind.Toggle, isOn: isOn);

        public static SettingsField Info(string label, string value) =>
            new SettingsField(label, value, SettingsFieldKind.ReadOnly);
    }
}
