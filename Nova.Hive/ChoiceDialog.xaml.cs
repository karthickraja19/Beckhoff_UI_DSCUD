using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Nova.Hive
{
    /// <summary>One option of a <see cref="ChoiceDialog"/>, pre-selected when <see cref="IsDefault"/>.</summary>
    public sealed class Choice
    {
        public Choice(string label, bool isDefault = false)
        {
            Label = label;
            IsDefault = isDefault;
        }

        public string Label { get; }

        public bool IsDefault { get; }
    }

    /// <summary>Picks one option from a closed list, e.g. CPK / GRR / SCS (slides 68-74).</summary>
    public partial class ChoiceDialog : Window
    {
        public ChoiceDialog(string header, string message, IEnumerable<Choice> choices)
        {
            InitializeComponent();

            HeaderText.Text = HiveText.T(header);
            MessageText.Text = HiveText.T(message ?? string.Empty);
            MessageText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;

            int index = 0;
            foreach (Choice choice in choices)
            {
                // Shown translated; SelectedLabel stays the English the procedures expect.
                _labels.Add(choice.Label);
                var button = new RadioButton
                {
                    Content = HiveText.T(choice.Label),
                    GroupName = "Choice",
                    Tag = index++,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 9),
                };
                button.Checked += (sender, e) =>
                {
                    SelectedIndex = (int)((RadioButton)sender).Tag;
                    SelectedLabel = _labels[SelectedIndex];
                    OkButton.IsEnabled = true;
                };
                Options.Children.Add(button);
                if (choice.IsDefault)
                    button.IsChecked = true;
            }
        }

        private readonly List<string> _labels = new List<string>();

        /// <summary>The chosen option's position, or -1.</summary>
        public int SelectedIndex { get; private set; } = -1;

        public string SelectedLabel { get; private set; }

        /// <summary>Shows the dialog over the main window; the chosen index, or -1 when cancelled.</summary>
        public static int Ask(string header, string message, params Choice[] choices)
        {
            var dialog = new ChoiceDialog(header, message, choices) { Owner = Application.Current.MainWindow };
            return dialog.ShowDialog() == true ? dialog.SelectedIndex : -1;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedIndex >= 0)
                DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
