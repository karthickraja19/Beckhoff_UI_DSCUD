using System.Windows;
using System.Windows.Controls;

namespace Nova.Hive
{
    /// <summary>What the operator picked in <see cref="HiveStateDialog"/>.</summary>
    public enum HiveStateChoice
    {
        None,
        Running,
        PlannedDowntime,
        ManualDowntime,
    }

    /// <summary>
    /// The state options the HIVE chip opens - Audio SW DFM v5.4, slide 49. Level 2/3
    /// only, and the state is not applied until the operator has badged in.
    /// </summary>
    public partial class HiveStateDialog : Window
    {
        /// <param name="currentState">One line saying what HIVE is in now.</param>
        /// <param name="runningBlocked">Why Running cannot be chosen, or null.</param>
        /// <param name="plannedBlocked">Why no planned downtime reason can be chosen, or null.</param>
        /// <param name="manualBlocked">Why Manual downtime cannot be chosen, or null.</param>
        public HiveStateDialog(string currentState, string runningBlocked, string plannedBlocked, string manualBlocked)
        {
            InitializeComponent();

            CurrentStateText.Text = HiveText.T(currentState);
            Configure(RunningButton, RunningNote, runningBlocked);
            Configure(PlannedButton, PlannedNote, plannedBlocked);
            Configure(ManualButton, ManualNote, manualBlocked);
        }

        public HiveStateChoice Choice { get; private set; }

        private static void Configure(Button button, TextBlock note, string blockedBecause)
        {
            button.IsEnabled = blockedBecause == null;
            note.Text = HiveText.T(blockedBecause);
            note.Visibility = blockedBecause == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Running_Click(object sender, RoutedEventArgs e) => Pick(HiveStateChoice.Running);

        private void PlannedDowntime_Click(object sender, RoutedEventArgs e) => Pick(HiveStateChoice.PlannedDowntime);

        private void ManualDowntime_Click(object sender, RoutedEventArgs e) => Pick(HiveStateChoice.ManualDowntime);

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Pick(HiveStateChoice choice)
        {
            Choice = choice;
            DialogResult = true;
        }
    }
}
